using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Otimizador.App.Overlay;
using Otimizador.App.Tweaks;

namespace Otimizador.App.GameMode;

/// <summary>
/// Modo jogo (desligado por padrão): quando um jogo ocupa a tela inteira, ativa o plano de energia Alto desempenho e
/// (opcional) o overlay; quando o jogo fecha, volta tudo como estava. Um jogo é reconhecido por ser uma janela que cobre
/// o monitor inteiro e usar DirectX/Vulkan, fora uma lista de navegadores e apresentadores de vídeo.
/// </summary>
public sealed partial class GameModeService : ObservableObject
{
    public static GameModeService Instance { get; } = new();

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "shellexperiencehost", "searchhost", "startmenuexperiencehost", "applicationframehost", "textinputhost",
        "lockapp", "widgets", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "vlc", "mpv", "wmplayer", "powerpnt",
        "teams", "ms-teams", "zoom", "obs64", "mstsc", "vmconnect", "otimizador",
    };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string? _previousPlan;
    private bool _overlayStartedByGame;
    private DateTime _lastSeen;
    private bool _loading = true;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _showOverlay = true;
    [ObservableProperty] private bool _gameActive;
    [ObservableProperty] private string _gameName = "";

    private GameModeService()
    {
        var s = Settings.Load();
        _enabled = s.GameModeEnabled;
        _showOverlay = s.GameModeOverlay;
        _loading = false;
        _timer.Tick += (_, _) => Tick();
        if (_enabled) _timer.Start();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_loading)
        {
            var s = Settings.Load();
            s.GameModeEnabled = value;
            s.Save();
        }
        if (value) _timer.Start();
        else
        {
            _timer.Stop();
            if (GameActive) EndGame();
        }
    }

    partial void OnShowOverlayChanged(bool value)
    {
        if (_loading) return;
        var s = Settings.Load();
        s.GameModeOverlay = value;
        s.Save();
    }

    private void Tick()
    {
        var game = DetectFullscreenGame();
        if (game is not null)
        {
            _lastSeen = DateTime.Now;
            if (!GameActive) StartGame(game);
        }
        else if (GameActive && DateTime.Now - _lastSeen > TimeSpan.FromSeconds(12))
        {
            EndGame();
        }
    }

    private void StartGame(string name)
    {
        _previousPlan = PowerPlans.Active();
        PowerPlans.ActivateHighPerformance();
        if (ShowOverlay && !OverlayService.Instance.IsVisible)
        {
            OverlayService.Instance.IsVisible = true;
            _overlayStartedByGame = true;
        }
        GameName = name;
        GameActive = true;
        EventLog.Add("game", $"Modo jogo ligado ({name})");
    }

    private void EndGame()
    {
        if (_previousPlan is not null) PowerPlans.Set(_previousPlan);
        if (_overlayStartedByGame) OverlayService.Instance.IsVisible = false;
        _overlayStartedByGame = false;
        EventLog.Add("game", $"Modo jogo desligado ({GameName})");
        GameActive = false;
        GameName = "";
    }

    /// <summary>Encerra o modo (restaurando o plano de energia) ao sair do app.</summary>
    public void Shutdown()
    {
        _timer.Stop();
        if (GameActive) EndGame();
    }

    // ---- Detecção ----
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private static readonly string[] GraphicsModules = { "d3d11", "d3d12", "d3d9", "dxgi", "vulkan-1", "opengl32" };

    /// <summary>Nome do processo em primeiro plano se ele cobre o monitor inteiro e usa API gráfica de jogo; senão null.</summary>
    public static string? DetectFullscreenGame()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return null;
            if (!GetWindowRect(hwnd, out var r)) return null;
            var mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(mon, ref info)) return null;

            // Janela maximizada com borda não cobre a barra de tarefas; tela cheia de jogo cobre o monitor todo.
            bool covers = r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top
                          && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom;
            if (!covers) return null;

            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName;
            if (Ignored.Contains(name)) return null;

            try
            {
                var loaded = proc.Modules.Cast<ProcessModule>().Select(m => Path.GetFileNameWithoutExtension(m.ModuleName).ToLowerInvariant()).ToHashSet();
                if (!GraphicsModules.Any(g => loaded.Contains(g))) return null;
            }
            catch
            {
                // não deu para listar os módulos (processo protegido): aceita pelo critério de tela cheia
            }
            return name;
        }
        catch
        {
            return null;
        }
    }
}
