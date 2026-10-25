using System.Runtime.InteropServices;

namespace Otimizador.App.Cleanup;

public sealed class CleanupCategory
{
    public required string Id { get; init; }
    public required string NamePt { get; init; }
    public required string NameEn { get; init; }
    public required string DescPt { get; init; }
    public required string DescEn { get; init; }
    public bool NeedsAdmin { get; init; }

    /// <summary>Selecionado por padrão? (a Lixeira e os caches de navegador ficam de fora: decisão do usuário.)</summary>
    public bool DefaultSelected { get; init; } = true;

    /// <summary>Só apaga o que não é alterado há mais de N dias (evita arquivos que estão em uso).</summary>
    public int MinAgeDays { get; init; }

    /// <summary>Pastas a limpar (o conteúdo é apagado, a pasta fica).</summary>
    public Func<IEnumerable<string>>? Roots { get; init; }

    /// <summary>Padrão de nome de arquivo dentro das pastas (ex.: thumbcache_*.db). Null = tudo.</summary>
    public string? FilePattern { get; init; }

    public bool IsRecycleBin { get; init; }

    public string Name => Loc.T(NamePt, NameEn);
    public string Description => Loc.T(DescPt, DescEn);
}

/// <summary>Limpezas seguras: só pastas conhecidas de cache/temporários, nunca documentos do usuário.</summary>
public static class CleanupService
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string Common => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private static IEnumerable<string> ChromiumCaches(string userData)
    {
        if (!Directory.Exists(userData)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(userData))
        {
            var name = Path.GetFileName(profile);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage" })
            {
                var p = Path.Combine(profile, sub);
                if (Directory.Exists(p)) yield return p;
            }
        }
    }

    private static IEnumerable<string> FirefoxCaches()
    {
        var profiles = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(profiles)) yield break;
        foreach (var p in Directory.EnumerateDirectories(profiles))
        {
            var c = Path.Combine(p, "cache2");
            if (Directory.Exists(c)) yield return c;
        }
    }

    public static IReadOnlyList<CleanupCategory> All { get; } = new CleanupCategory[]
    {
        new()
        {
            Id = "usertemp", MinAgeDays = 1,
            NamePt = "Temporários do usuário", NameEn = "User temporary files",
            DescPt = "Arquivos que programas deixaram na pasta Temp, com mais de 1 dia.", DescEn = "Files programs left in the Temp folder, older than 1 day.",
            Roots = () => new[] { Path.GetTempPath() },
        },
        new()
        {
            Id = "wintemp", MinAgeDays = 1, NeedsAdmin = true,
            NamePt = "Temporários do Windows", NameEn = "Windows temporary files",
            DescPt = "Pasta Windows\\Temp, com mais de 1 dia.", DescEn = "Windows\\Temp folder, older than 1 day.",
            Roots = () => new[] { Path.Combine(Win, "Temp") },
        },
        new()
        {
            Id = "winupdate", NeedsAdmin = true,
            NamePt = "Cache do Windows Update", NameEn = "Windows Update cache",
            DescPt = "Arquivos de atualização já baixados. O Windows baixa de novo se precisar.", DescEn = "Already downloaded update files. Windows downloads them again if needed.",
            Roots = () => new[] { Path.Combine(Win, @"SoftwareDistribution\Download") },
        },
        new()
        {
            Id = "delivery", NeedsAdmin = true,
            NamePt = "Otimização de Entrega", NameEn = "Delivery Optimization",
            DescPt = "Cache usado para compartilhar atualizações entre PCs.", DescEn = "Cache used to share updates between PCs.",
            Roots = () => new[] { Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") },
        },
        new()
        {
            Id = "thumbs",
            NamePt = "Miniaturas e ícones em cache", NameEn = "Thumbnail and icon cache",
            DescPt = "O Explorer recria as miniaturas quando precisar.", DescEn = "Explorer rebuilds thumbnails when needed.",
            Roots = () => new[] { Path.Combine(Local, @"Microsoft\Windows\Explorer") }, FilePattern = "*cache_*.db",
        },
        new()
        {
            Id = "wer", NeedsAdmin = true,
            NamePt = "Relatórios de erro do Windows", NameEn = "Windows error reports",
            DescPt = "Relatórios de travamentos já enviados ou pendentes.", DescEn = "Crash reports already sent or pending.",
            Roots = () => new[] { Path.Combine(Common, @"Microsoft\Windows\WER") },
        },
        new()
        {
            Id = "shaders",
            NamePt = "Cache de shaders (vídeo)", NameEn = "Shader cache (graphics)",
            DescPt = "DirectX, AMD e NVIDIA. Os jogos recriam o cache na próxima vez (pode demorar um pouco mais para abrir).",
            DescEn = "DirectX, AMD and NVIDIA. Games rebuild it next time (may take slightly longer to load).",
            Roots = () => new[]
            {
                Path.Combine(Local, "D3DSCache"), Path.Combine(Local, @"AMD\DxCache"), Path.Combine(Local, @"AMD\GLCache"),
                Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache"),
            },
        },
        new()
        {
            Id = "chrome", DefaultSelected = false,
            NamePt = "Cache do Google Chrome", NameEn = "Google Chrome cache",
            DescPt = "Só o cache de páginas. Senhas, histórico e favoritos não são tocados. Feche o navegador antes.",
            DescEn = "Page cache only. Passwords, history and bookmarks are untouched. Close the browser first.",
            Roots = () => ChromiumCaches(Path.Combine(Local, @"Google\Chrome\User Data")),
        },
        new()
        {
            Id = "edge", DefaultSelected = false,
            NamePt = "Cache do Microsoft Edge", NameEn = "Microsoft Edge cache",
            DescPt = "Só o cache de páginas. Feche o navegador antes.", DescEn = "Page cache only. Close the browser first.",
            Roots = () => ChromiumCaches(Path.Combine(Local, @"Microsoft\Edge\User Data")),
        },
        new()
        {
            Id = "firefox", DefaultSelected = false,
            NamePt = "Cache do Firefox", NameEn = "Firefox cache",
            DescPt = "Só o cache de páginas. Feche o navegador antes.", DescEn = "Page cache only. Close the browser first.",
            Roots = FirefoxCaches,
        },
        new()
        {
            Id = "recycle", DefaultSelected = false, IsRecycleBin = true,
            NamePt = "Lixeira", NameEn = "Recycle Bin",
            DescPt = "Apaga de vez o que está na Lixeira. Não dá para desfazer.", DescEn = "Permanently deletes what is in the Recycle Bin. This cannot be undone.",
        },
    };

    // ---- Lixeira (API do Windows) ----
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ShQueryRbInfo
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    private const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static IEnumerable<FileInfo> Files(CleanupCategory c)
    {
        var cutoff = DateTime.Now.AddDays(-c.MinAgeDays);
        foreach (var root in c.Roots?.Invoke() ?? Enumerable.Empty<string>())
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<FileInfo> files;
            try
            {
                files = new DirectoryInfo(root).EnumerateFiles(c.FilePattern ?? "*", Walk);
            }
            catch
            {
                continue;
            }
            foreach (var f in files)
            {
                bool ok;
                try
                {
                    ok = c.MinAgeDays == 0 || f.LastWriteTime < cutoff;
                }
                catch
                {
                    ok = false;
                }
                if (ok) yield return f;
            }
        }
    }

    /// <summary>Quanto esta categoria ocupa (e seria liberado).</summary>
    public static long Measure(CleanupCategory c, CancellationToken ct = default)
    {
        if (c.IsRecycleBin)
        {
            var info = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }
        long total = 0;
        foreach (var f in Files(c))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                total += f.Length;
            }
            catch
            {
                // arquivo sumiu
            }
        }
        return total;
    }

    /// <summary>Apaga e retorna os bytes realmente liberados (arquivos em uso são ignorados).</summary>
    public static long Clean(CleanupCategory c, CancellationToken ct = default)
    {
        if (c.IsRecycleBin)
        {
            long before = Measure(c);
            SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            return before;
        }

        long freed = 0;
        foreach (var f in Files(c).ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long len = f.Length;
                f.Delete();
                freed += len;
            }
            catch
            {
                // em uso ou sem permissão: segue
            }
        }

        // remove subpastas que ficaram vazias (a pasta raiz permanece)
        foreach (var root in c.Roots?.Invoke() ?? Enumerable.Empty<string>())
        {
            if (!Directory.Exists(root) || c.FilePattern is not null) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                             .OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                    }
                    catch
                    {
                        // não vazia ou em uso
                    }
                }
            }
            catch
            {
                // sem acesso
            }
        }
        return freed;
    }
}
