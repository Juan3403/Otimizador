using CommunityToolkit.Mvvm.ComponentModel;

namespace Otimizador.App.Overlay;

public sealed record ScaleOption(int Percent)
{
    public double Factor => Percent / 100.0;
    public string Label => $"{Percent}%";
}

/// <summary>
/// Controla o overlay de métricas (janela flutuante sempre no topo). Guarda as preferências e atualiza
/// as linhas de texto a cada leitura de sensores.
/// </summary>
public sealed partial class OverlayService : ObservableObject
{
    public static OverlayService Instance { get; } = new();

    private OverlayWindow? _window;
    private bool _loading = true;

    /// <summary>Janela do overlay quando visível (usada pelas capturas de teste).</summary>
    public System.Windows.Window? Window => _window;

    public ScaleOption[] Scales { get; } = { new(100), new(125), new(150), new(200) };

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _showCpu = true;
    [ObservableProperty] private bool _showGpu = true;
    [ObservableProperty] private bool _showRam = true;
    [ObservableProperty] private bool _showDisk;
    [ObservableProperty] private ScaleOption _scale;

    [ObservableProperty] private string _cpuLine = "";
    [ObservableProperty] private string _gpuLine = "";
    [ObservableProperty] private string _ramLine = "";
    [ObservableProperty] private string _diskLine = "";

    private OverlayService()
    {
        var s = Settings.Load();
        _isLocked = s.OverlayLocked;
        _showCpu = s.OverlayCpu;
        _showGpu = s.OverlayGpu;
        _showRam = s.OverlayRam;
        _showDisk = s.OverlayDisk;
        _scale = Scales.FirstOrDefault(x => x.Percent == s.OverlayScale) ?? Scales[0];
        _loading = false;
        TelemetryService.Instance.Updated += OnTelemetry;
    }

    public void Toggle() => IsVisible = !IsVisible;

    partial void OnIsVisibleChanged(bool value)
    {
        if (value)
        {
            if (TelemetryService.Instance.Latest is { } s) OnTelemetry(s);
            _window = new OverlayWindow();
            _window.Closed += (_, _) =>
            {
                _window = null;
                if (IsVisible) IsVisible = false;
            };
            _window.Show();
        }
        else
        {
            var w = _window;
            _window = null;
            w?.Close();
        }
    }

    partial void OnIsLockedChanged(bool value)
    {
        _window?.ApplyLock();
        Save();
    }

    partial void OnShowCpuChanged(bool value) => Save();
    partial void OnShowGpuChanged(bool value) => Save();
    partial void OnShowRamChanged(bool value) => Save();
    partial void OnShowDiskChanged(bool value) => Save();
    partial void OnScaleChanged(ScaleOption value) => Save();

    /// <summary>Guarda a posição depois que o usuário arrasta a janela.</summary>
    public void SavePosition(double x, double y)
    {
        var s = Settings.Load();
        s.OverlayX = x;
        s.OverlayY = y;
        s.Save();
    }

    private void Save()
    {
        if (_loading) return;
        var s = Settings.Load();
        s.OverlayLocked = IsLocked;
        s.OverlayCpu = ShowCpu;
        s.OverlayGpu = ShowGpu;
        s.OverlayRam = ShowRam;
        s.OverlayDisk = ShowDisk;
        s.OverlayScale = Scale.Percent;
        s.Save();
    }

    private static string P(float? v, int width) => (v is null ? "--" : $"{v.Value:0}").PadLeft(width);

    private void OnTelemetry(TelemetrySample s)
    {
        if (!IsVisible) return;
        string ghz = s.CpuClock is { } c ? $"{c / 1000:0.0}" : "--";
        CpuLine = $"CPU {P(s.CpuLoad, 3)}%  {P(s.CpuTemp, 3)}°C  {ghz}GHz  {P(s.CpuPower, 3)}W";
        GpuLine = $"GPU {P(s.GpuLoad, 3)}%  {P(s.GpuTemp, 3)}°C  {P(s.GpuClock, 4)}MHz  {P(s.GpuPower, 3)}W";
        RamLine = s.RamUsedGb is { } u && s.RamTotalGb is { } t
            ? $"RAM {P(s.RamLoad, 3)}%  {u:0.0}/{t:0.0}GB"
            : $"RAM {P(s.RamLoad, 3)}%";
        DiskLine = $"DSK {P(s.DiskUsed, 3)}%  {P(s.DiskTemp, 3)}°C";
    }
}

