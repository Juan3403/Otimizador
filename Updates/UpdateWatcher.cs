using System.Windows.Threading;
using Otimizador.App.Alerts;

namespace Otimizador.App.Updates;

/// <summary>Verifica sozinho (no máximo uma vez por dia) se há versão nova, e avisa o resto do app.</summary>
public static class UpdateWatcher
{
    public static UpdateInfo? Available { get; private set; }

    public static event Action? Changed;

    public static void Set(UpdateInfo? info)
    {
        Available = info;
        Changed?.Invoke();
    }

    /// <summary>Agenda a verificação automática (se ligada e se o repositório estiver configurado).</summary>
    public static void Start()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            var s = Settings.Load();
            if (!s.AutoCheckUpdates || AppInfo.Repo.Length == 0) return;
            if (s.LastUpdateCheck is { } last && DateTime.Now - last < TimeSpan.FromHours(20)) return;

            var result = await UpdateService.CheckAsync();
            if (result.State == UpdateState.Available && result.Info is { } info)
            {
                Set(info);
                ToastWindow.Show(Loc.T("Atualização disponível", "Update available"),
                    $"{Loc.T("Versão", "Version")} {info.Version.Major}.{info.Version.Minor}.{info.Version.Build}. {Loc.T("Abra Ajustes para atualizar.", "Open Settings to update.")}");
            }
        };
        timer.Start();
    }
}
