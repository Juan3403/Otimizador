using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Updates;

namespace Otimizador.App.ViewModels;

public sealed record Credit(string Name, string Use, string License, string Url);

public partial class AboutViewModel : PageViewModel
{
    public override string TitleKey => "Nav_About";

    public override string SubtitleKey => "Sub_About";

    public string VersionText => $"{Loc.T("Versão", "Version")} {AppInfo.VersionText}";

    public string Author => AppInfo.Author;

    public string AuthorHandle => "@" + AppInfo.AuthorGitHub;

    public Credit[] Credits { get; } =
    {
        new("LibreHardwareMonitor", Loc.T("leitura de sensores", "sensor reading"), "MPL-2.0", "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor"),
        new("Vortice.Windows", Loc.T("teste de GPU (Direct3D 11)", "GPU test (Direct3D 11)"), "MIT", "https://github.com/amerkoleci/Vortice.Windows"),
        new("H.NotifyIcon", Loc.T("ícone na bandeja", "tray icon"), "MIT", "https://github.com/HavenDV/H.NotifyIcon"),
        new("CommunityToolkit.Mvvm", Loc.T("estrutura da interface", "UI structure"), "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("PawnIO", Loc.T("driver opcional da temperatura de CPUs AMD", "optional driver for AMD CPU temperature"), "GPL-2.0", "https://github.com/namazso/PawnIO.Setup"),
    };

    private static void Open(string url)
    {
        try
        {
            if (UpdateService.IsAllowedUrl(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // sem navegador padrão
        }
    }

    [RelayCommand] private static void OpenAuthor() => Open(AppInfo.AuthorUrl);
    [RelayCommand] private static void OpenRepo() => Open(AppInfo.RepoUrl);
    [RelayCommand] private static void OpenIssues() => Open(AppInfo.RepoUrl + "/issues");
    [RelayCommand] private static void OpenReleases() => Open(AppInfo.RepoUrl + "/releases");
    [RelayCommand] private static void OpenLicense() => Open(AppInfo.RepoUrl + "/blob/main/LICENSE");
    [RelayCommand] private static void OpenCredit(Credit credit) => Open(credit.Url);
}
