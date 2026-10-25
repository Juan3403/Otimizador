using System.Windows;

namespace Otimizador.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var renderOnly = Array.IndexOf(e.Args, "--rendertest");
        if (renderOnly >= 0)
        {
            var path = renderOnly + 1 < e.Args.Length ? e.Args[renderOnly + 1] : "rendertest.txt";
            Shutdown(SelfTest.RunRender(path));
            return;
        }

        var stressOnly = Array.IndexOf(e.Args, "--stresstest");
        if (stressOnly >= 0)
        {
            var path = stressOnly + 1 < e.Args.Length ? e.Args[stressOnly + 1] : "stresstest.txt";
            Shutdown(SelfTest.RunStress(path));
            return;
        }

        var selfTest = Array.IndexOf(e.Args, "--selftest");
        if (selfTest >= 0)
        {
            var path = selfTest + 1 < e.Args.Length ? e.Args[selfTest + 1] : "selftest.txt";
            var seconds = selfTest + 2 < e.Args.Length && int.TryParse(e.Args[selfTest + 2], out var s) ? s : 6;
            Shutdown(SelfTest.Run(path, seconds));
            return;
        }

        ThemeService.Init();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow();
        MainWindow = window;

        // Abre direto na bandeja se pedido (--minimized, usado pela tarefa de inicialização com o Windows, ou a opção em Ajustes)
        bool minimized = e.Args.Contains("--minimized") || Settings.Load().StartMinimized;
        Tray.TrayService.Init(window);
        if (minimized && Tray.TrayService.IsActive)
        {
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        }
        else
        {
            window.Show();
        }

        TelemetryService.Instance.Start();
        HistoryService.Instance.Start();
        _ = GameMode.GameModeService.Instance;          // vigia jogos se o modo estiver ligado
        _ = Cleanup.ScheduledCleanupService.Instance;   // limpeza semanal se estiver ligada
        Inventory.InventoryService.Start();             // publica o inventário se estiver ligado
        Updates.UpdateWatcher.Start();                  // procura versão nova no GitHub (no máximo 1 vez por dia)
        Hardware.PawnIoInstaller.StartFirstRunPrompt(); // oferece o driver da temperatura (AMD) se faltar e estiver no pacote
        _ = Alerts.AlertService.Instance; // começa a vigiar temperaturas e disco

        var snap = Array.IndexOf(e.Args, "--snapshots");
        if (snap >= 0)
        {
            var dir = snap + 1 < e.Args.Length ? e.Args[snap + 1] : "snapshots";
            _ = SnapshotTool.RunAsync(window, dir);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        HistoryService.Instance.Flush();
        TelemetryService.Instance.Stop();
        AppServices.Sensors.Dispose();
        base.OnExit(e);
    }
}



