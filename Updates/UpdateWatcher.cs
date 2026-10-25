using System.Windows.Threading;
using Otimizador.App.Alerts;

namespace Otimizador.App.Updates;

/// <summary>Verifica sozinho se há versão nova (ao abrir e depois a cada 6 h) e avisa o resto do app.</summary>
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
            timer.Interval = TimeSpan.FromHours(6);
            var s = Settings.Load();
            if (!s.AutoCheckUpdates || AppInfo.Repo.Length == 0) return;

            // O chip no menu sempre aparece; o aviso (toast) sai uma vez por versão.
            var result = await UpdateService.CheckAsync();
            if (result.State == UpdateState.Available && result.Info is { } info)
            {
                Set(info);
                string version = $"{info.Version.Major}.{info.Version.Minor}.{info.Version.Build}";
                if (s.LastNotifiedVersion == version) return;
                s.LastNotifiedVersion = version;
                s.Save();
                ToastWindow.Show(Loc.T("Atualização disponível", "Update available"),
                    $"{Loc.T("Versão", "Version")} {version}. {Loc.T("Abra Ajustes para atualizar.", "Open Settings to update.")}");
            }
        };
        timer.Start();
    }
}
