using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Otimizador.App.Updates;

public sealed record UpdateInfo(
    Version Version, string Tag, string Notes, string PageUrl,
    string? InstallerUrl, string? ZipUrl, string? ChecksumsUrl);

public enum UpdateState { NotConfigured, UpToDate, Available, NoRelease, Error }

public sealed record UpdateCheck(UpdateState State, UpdateInfo? Info = null, string Message = "");

/// <summary>
/// Atualização pelas Releases do GitHub: consulta a última versão, baixa o instalador (ou o zip da versão portátil),
/// confere o SHA-256 publicado na mesma Release e só então instala. Só aceita endereços do GitHub.
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Otimizador", AppInfo.VersionText));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Lê o JSON de uma Release do GitHub. Retorna null se o formato não for reconhecido.</summary>
    public static UpdateInfo? ParseRelease(string json)
    {
        try
        {
            return ParseReleaseCore(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static UpdateInfo? ParseReleaseCore(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagEl)) return null;
        var tag = tagEl.GetString() ?? "";
        if (!TryParseVersion(tag, out var version)) return null;

        string? installer = null, zip = null, sums = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString();
                if (url is null || !IsAllowedUrl(url)) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) installer = url;
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip = url;
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sums = url;
            }
        }
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return new UpdateInfo(version, tag, notes, page, installer, zip, sums);
    }

    public static bool TryParseVersion(string tag, out Version version)
    {
        var m = Regex.Match(tag.Trim(), @"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success)
        {
            version = new Version(0, 0, 0);
            return false;
        }
        version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        return true;
    }

    /// <summary>Só https e só domínios do GitHub (a Release do repositório e o armazenamento dela).</summary>
    public static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && (u.Host == "github.com" || u.Host.EndsWith(".github.com") || u.Host.EndsWith(".githubusercontent.com"));

    public static async Task<UpdateCheck> CheckAsync(CancellationToken ct = default, string? repoOverride = null)
    {
        var repo = repoOverride ?? AppInfo.Repo;
        if (repo.Length == 0) return new UpdateCheck(UpdateState.NotConfigured);
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheck(UpdateState.NoRelease);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheck(UpdateState.Error, Message: $"GitHub: {(int)response.StatusCode} {response.ReasonPhrase}");

            var info = ParseRelease(await response.Content.ReadAsStringAsync(ct));
            if (info is null) return new UpdateCheck(UpdateState.Error, Message: "Release sem número de versão");

            var s = Settings.Load();
            s.LastUpdateCheck = DateTime.Now;
            s.Save();
            return info.Version > AppInfo.Version
                ? new UpdateCheck(UpdateState.Available, info)
                : new UpdateCheck(UpdateState.UpToDate, info);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new UpdateCheck(UpdateState.Error, Message: Loc.T(
                "Sem conexão com a internet (ou o GitHub está fora do ar). Tente de novo mais tarde.",
                "No internet connection (or GitHub is down). Try again later."));
        }
        catch (Exception ex)
        {
            return new UpdateCheck(UpdateState.Error, Message: ex.Message);
        }
    }

    public enum InstallKind { Installed, Portable, Manual }

    public static InstallKind CurrentInstall()
    {
        if (AppServices.IsPortable) return InstallKind.Portable;
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"))) return InstallKind.Installed;
        return InstallKind.Manual;
    }

    /// <summary>Procura o hash do arquivo no SHA256SUMS.txt (formato "hash  nome").</summary>
    public static string? FindHash(string sumsText, string fileName)
    {
        foreach (var line in sumsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && m.Groups[2].Value.Trim().Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Baixa o arquivo da Release e confere o SHA-256. Lança exceção se não bater.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, string assetUrl, IProgress<double>? progress, CancellationToken ct)
    {
        if (!IsAllowedUrl(assetUrl)) throw new InvalidOperationException("endereço de download não permitido");
        if (info.ChecksumsUrl is null) throw new InvalidOperationException("a Release não tem SHA256SUMS.txt; por segurança a atualização foi recusada");

        string fileName = Uri.UnescapeDataString(Path.GetFileName(new Uri(assetUrl).AbsolutePath));
        string sums = await Http.GetStringAsync(info.ChecksumsUrl, ct);
        string expected = FindHash(sums, fileName) ?? throw new InvalidOperationException($"sem checksum de {fileName} no SHA256SUMS.txt");

        var dir = Path.Combine(Path.GetTempPath(), "otimizador-update-" + info.Version);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, fileName);

        using (var response = await Http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(target);
            var buffer = new byte[1 << 16];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report(read * 100.0 / total);
            }
        }

        string actual;
        await using (var fs = File.OpenRead(target))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(target);
            throw new InvalidOperationException("o arquivo baixado não confere com o checksum: atualização recusada");
        }
        return target;
    }

    /// <summary>Roda o instalador em silêncio, na mesma pasta do app atual, e reabre o app no fim.</summary>
    public static void RunInstaller(string setupPath)
    {
        string dir = AppContext.BaseDirectory.TrimEnd('\\');
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string scope = dir.StartsWith(local, StringComparison.OrdinalIgnoreCase) ? "/CURRENTUSER" : "/ALLUSERS";
        Process.Start(new ProcessStartInfo(setupPath,
            $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS {scope} /DIR=\"{dir}\" /relaunch=1")
        { UseShellExecute = true });
    }

    /// <summary>Versão portátil: extrai o zip, e um script troca os arquivos depois que o app fechar (preserva a pasta Dados).</summary>
    public static void RunPortableUpdate(string zipPath)
    {
        string extract = Path.Combine(Path.GetDirectoryName(zipPath)!, "extraido");
        if (Directory.Exists(extract)) Directory.Delete(extract, true);
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extract);
        string src = Directory.EnumerateFiles(extract, "Otimizador.exe", SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault()
                     ?? throw new InvalidOperationException("o zip não contém o Otimizador.exe");

        string dest = AppContext.BaseDirectory.TrimEnd('\\');
        string script = Path.Combine(Path.GetDirectoryName(zipPath)!, "atualizar.ps1");
        File.WriteAllText(script, """
            param([int]$ProcessId, [string]$Src, [string]$Dest)
            Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue
            robocopy $Src $Dest /E /XD Dados /XF portable.txt /R:3 /W:1 /NFL /NDL /NJH /NJS | Out-Null
            Start-Process (Join-Path $Dest 'Otimizador.exe')
            """);
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -ProcessId {Environment.ProcessId} -Src \"{src}\" -Dest \"{dest}\"")
        { UseShellExecute = false, CreateNoWindow = true });
    }
}

