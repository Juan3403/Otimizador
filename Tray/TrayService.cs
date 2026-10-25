using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Otimizador.App.GameMode;
using Otimizador.App.Overlay;

namespace Otimizador.App.Tray;

/// <summary>Ícone na bandeja do relógio: abre o app, liga o overlay e o modo jogo, e mostra CPU/GPU no balão.</summary>
public static class TrayService
{
    private static TaskbarIcon? _icon;
    private static Window? _main;

    /// <summary>true quando o usuário escolheu "Sair" (aí a janela fecha de verdade em vez de ir para a bandeja).</summary>
    public static bool Exiting { get; private set; }

    public static bool IsActive => _icon is not null;

    /// <summary>true se o Windows realmente criou o ícone na bandeja.</summary>
    public static bool IsCreated => _icon?.IsCreated == true;

    /// <summary>Último erro ao criar o ícone (para diagnóstico).</summary>
    public static string? LastError { get; private set; }

    public static void Init(Window main)
    {
        _main = main;
        var settings = Settings.Load();
        if (!settings.TrayEnabled) return;

        try
        {
            _icon = new TaskbarIcon { ToolTipText = AppInfo.Name, NoLeftClickDelay = true };
            using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Otimizador.ico")).Stream)
                _icon.Icon = new System.Drawing.Icon(stream);
            _icon.TrayMouseDoubleClick += (_, _) => ShowMain();
            _icon.ForceCreate(false);
            Rebuild();

            Loc.Instance.PropertyChanged += OnLocChanged;
            TelemetryService.Instance.Updated += OnSample;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _icon?.Dispose();
            _icon = null; // sem bandeja (ex.: Explorer reiniciando): o app segue normal
        }
    }

    private static void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Loc.Lang)) Rebuild();
    }

    private static void OnSample(TelemetrySample s)
    {
        if (_icon is null) return;
        var parts = new List<string> { AppInfo.Name };
        if (s.CpuLoad is { } cl) parts.Add($"CPU {cl:0}%{(s.CpuTemp is { } ct ? $" {ct:0}°C" : "")}");
        if (s.GpuLoad is { } gl) parts.Add($"GPU {gl:0}%{(s.GpuTemp is { } gt ? $" {gt:0}°C" : "")}");
        if (s.RamLoad is { } r) parts.Add($"RAM {r:0}%");
        var text = string.Join(" · ", parts);
        _icon.ToolTipText = text.Length > 120 ? text[..120] : text;
    }

    private static void Rebuild()
    {
        if (_icon is null) return;
        var menu = new ContextMenu();

        var open = new MenuItem { Header = Loc.T("Abrir Otimizador", "Open Optimizer"), FontWeight = FontWeights.Bold };
        open.Click += (_, _) => ShowMain();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());

        var overlay = new MenuItem { Header = Loc.T("Overlay de métricas", "Metrics overlay"), IsCheckable = true, IsChecked = OverlayService.Instance.IsVisible };
        overlay.Click += (_, _) => OverlayService.Instance.IsVisible = overlay.IsChecked;
        menu.Items.Add(overlay);

        var game = new MenuItem { Header = Loc.T("Modo jogo automático", "Automatic game mode"), IsCheckable = true, IsChecked = GameModeService.Instance.Enabled };
        game.Click += (_, _) => GameModeService.Instance.Enabled = game.IsChecked;
        menu.Items.Add(game);

        // reflete mudanças feitas na janela
        menu.Opened += (_, _) =>
        {
            overlay.IsChecked = OverlayService.Instance.IsVisible;
            game.IsChecked = GameModeService.Instance.Enabled;
        };

        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = Loc.T("Sair", "Exit") };
        exit.Click += (_, _) => Exit();
        menu.Items.Add(exit);

        _icon.ContextMenu = menu;
    }

    public static void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public static void Exit()
    {
        Exiting = true;
        _main?.Close();
        Application.Current.Shutdown();
    }

    public static void Dispose()
    {
        TelemetryService.Instance.Updated -= OnSample;
        _icon?.Dispose();
        _icon = null;
    }
}

