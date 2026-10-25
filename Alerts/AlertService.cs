using System.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Otimizador.App.Alerts;

/// <summary>
/// Avisa na tela (e com som) quando a CPU/GPU esquenta demais, o disco está quase cheio ou a memória acaba.
/// Cada alerta só repete depois de um tempo, para não incomodar.
/// </summary>
public sealed partial class AlertService : ObservableObject
{
    public static AlertService Instance { get; } = new();

    private readonly Dictionary<string, DateTime> _lastShown = new();
    private readonly Dictionary<string, int> _streak = new();
    private bool _loading = true;

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private int _cpuLimit = 90;
    [ObservableProperty] private int _gpuLimit = 85;
    [ObservableProperty] private int _diskMinFree = 10;
    [ObservableProperty] private int _ramLimit = 95;

    private AlertService()
    {
        var s = Settings.Load();
        _enabled = s.AlertsEnabled;
        _cpuLimit = s.AlertCpu;
        _gpuLimit = s.AlertGpu;
        _diskMinFree = s.AlertDiskFree;
        _ramLimit = s.AlertRam;
        _loading = false;
        TelemetryService.Instance.Updated += OnSample;
    }

    partial void OnEnabledChanged(bool value) => Save();
    partial void OnCpuLimitChanged(int value) => Save();
    partial void OnGpuLimitChanged(int value) => Save();
    partial void OnDiskMinFreeChanged(int value) => Save();
    partial void OnRamLimitChanged(int value) => Save();

    private void Save()
    {
        if (_loading) return;
        var s = Settings.Load();
        s.AlertsEnabled = Enabled;
        s.AlertCpu = CpuLimit;
        s.AlertGpu = GpuLimit;
        s.AlertDiskFree = DiskMinFree;
        s.AlertRam = RamLimit;
        s.Save();
    }

    /// <summary>Ajusta um limite em passos (usado pelos botões − e + da tela). Formato: "cpu:+5".</summary>
    [RelayCommand]
    private void Adjust(string spec)
    {
        var p = spec.Split(':');
        if (p.Length != 2 || !int.TryParse(p[1], out var delta)) return;
        switch (p[0])
        {
            case "cpu": CpuLimit = Math.Clamp(CpuLimit + delta, 50, 105); break;
            case "gpu": GpuLimit = Math.Clamp(GpuLimit + delta, 50, 105); break;
            case "disk": DiskMinFree = Math.Clamp(DiskMinFree + delta, 1, 50); break;
            case "ram": RamLimit = Math.Clamp(RamLimit + delta, 50, 99); break;
        }
    }

    [RelayCommand]
    private void Test() => Raise("test", Loc.T("Alert_Test"), force: true);

    private void OnSample(TelemetrySample s)
    {
        if (!Enabled) return;
        Check("cpu", s.CpuTemp is { } c && c >= CpuLimit, 3, TimeSpan.FromMinutes(10),
            () => $"CPU {s.CpuTemp:0} °C ({Loc.T("limite", "limit")} {CpuLimit} °C)");
        Check("gpu", s.GpuTemp is { } g && g >= GpuLimit, 3, TimeSpan.FromMinutes(10),
            () => $"GPU {s.GpuTemp:0} °C ({Loc.T("limite", "limit")} {GpuLimit} °C)");
        Check("ram", s.RamLoad is { } r && r >= RamLimit, 30, TimeSpan.FromMinutes(15),
            () => $"RAM {s.RamLoad:0}% ({Loc.T("limite", "limit")} {RamLimit}%)");
        Check("disk", s.DiskUsed is { } d && 100 - d <= DiskMinFree, 1, TimeSpan.FromHours(6),
            () => $"{Loc.T("Disco quase cheio", "Disk almost full")}: {100 - s.DiskUsed:0}% {Loc.T("livre", "free")}");
    }

    private void Check(string key, bool bad, int needed, TimeSpan cooldown, Func<string> message)
    {
        if (!bad)
        {
            _streak[key] = 0;
            return;
        }
        int n = _streak.GetValueOrDefault(key) + 1;
        _streak[key] = n;
        if (n < needed) return;
        if (_lastShown.TryGetValue(key, out var last) && DateTime.Now - last < cooldown) return;
        Raise(key, message(), force: false);
    }

    private void Raise(string key, string text, bool force)
    {
        _lastShown[key] = DateTime.Now;
        if (!force) EventLog.Add("alert", text);
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch
        {
            // sem placa de som
        }
        ToastWindow.Show(Loc.T("Alert_Title"), text);
    }
}
