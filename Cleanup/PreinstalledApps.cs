using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Otimizador.App.Cleanup;

public sealed record PreinstalledApp(string PackageName, string NamePt, string NameEn)
{
    public string Name => Loc.T(NamePt, NameEn);
}

/// <summary>
/// Remove aplicativos que vêm com o Windows e a maioria das pessoas não usa. Só os da lista abaixo (nunca componentes do sistema).
/// Dá para reinstalar qualquer um pela Microsoft Store.
/// </summary>
public static class PreinstalledApps
{
    public static IReadOnlyList<PreinstalledApp> Catalog { get; } = new PreinstalledApp[]
    {
        new("Microsoft.BingNews", "Notícias (Bing)", "News (Bing)"),
        new("Microsoft.BingWeather", "Clima (Bing)", "Weather (Bing)"),
        new("Microsoft.BingSearch", "Pesquisa Bing", "Bing Search"),
        new("Microsoft.GetHelp", "Obter Ajuda", "Get Help"),
        new("Microsoft.Getstarted", "Dicas", "Tips"),
        new("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection", "Microsoft Solitaire Collection"),
        new("Clipchamp.Clipchamp", "Clipchamp (editor de vídeo)", "Clipchamp (video editor)"),
        new("Microsoft.YourPhone", "Vínculo com o Celular", "Phone Link"),
        new("Microsoft.Todos", "Microsoft To Do", "Microsoft To Do"),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", "Power Automate"),
        new("MSTeams", "Microsoft Teams (pessoal)", "Microsoft Teams (personal)"),
        new("Microsoft.WindowsFeedbackHub", "Central de Comentários", "Feedback Hub"),
        new("Microsoft.ZuneMusic", "Media Player", "Media Player"),
        new("Microsoft.ZuneVideo", "Filmes e TV", "Movies & TV"),
        new("Microsoft.People", "Pessoas", "People"),
        new("Microsoft.WindowsMaps", "Mapas", "Maps"),
        new("Microsoft.SkypeApp", "Skype", "Skype"),
        new("Microsoft.MixedReality.Portal", "Portal de Realidade Misturada", "Mixed Reality Portal"),
        new("Microsoft.Microsoft3DViewer", "Visualizador 3D", "3D Viewer"),
    };

    private static string? RunPs(string command, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var err = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(timeoutMs);
            _ = err.Result;
            return output;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Quais itens do catálogo estão instalados para o usuário atual.</summary>
    public static List<PreinstalledApp> Installed()
    {
        var names = string.Join(",", Catalog.Select(a => $"'{a.PackageName}'"));
        var json = RunPs($"@(Get-AppxPackage | Where-Object {{ @({names}) -contains $_.Name }} | Select-Object -ExpandProperty Name) | ConvertTo-Json -Compress", 60_000);
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var found = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string?>(new[] { doc.RootElement.GetString() }, StringComparer.OrdinalIgnoreCase);
            return Catalog.Where(a => found.Contains(a.PackageName)).ToList();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>Remove o aplicativo do usuário atual. Retorna null se deu certo ou a mensagem de erro.</summary>
    public static string? Remove(PreinstalledApp app)
    {
        if (!Catalog.Any(a => a.PackageName == app.PackageName)) return "fora da lista";
        var output = RunPs($"Get-AppxPackage -Name '{app.PackageName}' | Remove-AppxPackage -ErrorAction Stop; 'OK'", 180_000);
        return output?.Contains("OK") == true ? null : "falhou";
    }
}
