using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Bench;
using Otimizador.App.Hardware;
using Otimizador.App.Overlay;

namespace Otimizador.App.ViewModels;

public enum MetricKind { Cpu, Gpu, Ram, Disk }

public partial class MetricCard : ObservableObject
{
    public required MetricKind Kind { get; init; }
    public required string Glyph { get; init; }
    public required LocText Title { get; init; }

    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _percentText = "—";
    [ObservableProperty] private double _tempValue = double.NaN;
    [ObservableProperty] private string _tempText = "—";
    [ObservableProperty] private string _line1 = "";
    [ObservableProperty] private string _line2 = "";
    [ObservableProperty] private double[] _history = Array.Empty<double>();
}

public sealed record ResultChip(LocText Label, string Value);

public sealed record ProcessItem(string Name, string MemoryText, double Fraction);

public partial class DashboardViewModel : PageViewModel
{
    private readonly Action<string> _navigate;

    public override string TitleKey => "Nav_Dashboard";

    public override string SubtitleKey => "Sub_Dashboard";

    public MetricCard Cpu { get; } = new() { Kind = MetricKind.Cpu, Glyph = "", Title = new LocText("Mon_Cpu") };
    public MetricCard Gpu { get; } = new() { Kind = MetricKind.Gpu, Glyph = "", Title = new LocText("Mon_Gpu") };
    public MetricCard Ram { get; } = new() { Kind = MetricKind.Ram, Glyph = "", Title = new LocText("Mon_Ram") };
    public MetricCard Disk { get; } = new() { Kind = MetricKind.Disk, Glyph = "", Title = new LocText("Dash_Disk") };

    public MetricCard[] Cards => new[] { Cpu, Gpu, Ram, Disk };

    public ObservableCollection<ResultChip> Results { get; } = new();

    public ObservableCollection<ProcessItem> TopProcesses { get; } = new();

    private readonly DispatcherTimer _processTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private string _systemLine = "";
    [ObservableProperty] private double[] _cpuLoadHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuLoadHistory = Array.Empty<double>();
    [ObservableProperty] private bool _tempAvailable = true;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _uptimeText = "";

    public DashboardViewModel(Action<string> navigate)
    {
        _navigate = navigate;
        Ram.TempText = "";
        TelemetryService.Instance.Updated += OnUpdated;
        if (TelemetryService.Instance.Latest is { } s) OnUpdated(s);
        LoadResults();
        _ = LoadSummaryAsync();

        UpdateUptime();
        _processTimer.Tick += async (_, _) => { UpdateUptime(); await RefreshProcessesAsync(); };
        _processTimer.Start();
        _ = RefreshProcessesAsync();
    }

    private void UpdateUptime()
    {
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        string t = up.TotalDays >= 1 ? $"{(int)up.TotalDays} d {up.Hours} h" : $"{up.Hours} h {up.Minutes} min";
        UptimeText = $"{Loc.T("Dash_Uptime")} {t}";
    }

    /// <summary>Agrupa processos de mesmo nome (navegadores têm dezenas) e lista os que mais usam memória.</summary>
    private async Task RefreshProcessesAsync()
    {
        var top = await Task.Run(() =>
        {
            var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    totals[p.ProcessName] = totals.GetValueOrDefault(p.ProcessName) + p.WorkingSet64;
                }
                catch
                {
                    // processo protegido ou já encerrado
                }
                finally
                {
                    p.Dispose();
                }
            }
            return totals.OrderByDescending(kv => kv.Value).Take(6).ToList();
        });
        if (top.Count == 0) return;
        double max = top[0].Value;
        TopProcesses.Clear();
        foreach (var kv in top)
        {
            double mb = kv.Value / 1048576.0;
            string text = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0} MB";
            TopProcesses.Add(new ProcessItem(kv.Key, text, kv.Value / max * 100));
        }
    }

    private async Task LoadSummaryAsync()
    {
        var sections = await HardwareInfoService.GetAsync();
        CpuName = HardwareInfoService.Headline(sections, "cpu") ?? "";
        GpuName = HardwareInfoService.Headline(sections, "gpu") ?? "";
        var ram = HardwareInfoService.Headline(sections, "memory");
        var os = HardwareInfoService.Headline(sections, "system");
        SystemLine = string.Join("  ·  ", new[] { ram is null ? null : $"{ram} RAM", os }.Where(x => !string.IsNullOrEmpty(x)));
    }

    private void LoadResults()
    {
        Results.Clear();
        var h = BenchHistory.Load();
        void Add(string key, string labelKey, string fmt)
        {
            if (h.TryGetValue(key, out var v)) Results.Add(new ResultChip(new LocText(labelKey), v.ToString(fmt)));
        }
        Add("Test_CpuSingle", "Test_CpuSingle", "0");
        Add("Test_CpuMulti", "Test_CpuMulti", "0");
        Add("Test_Ram", "Test_Ram", "0.0");
        Add("Test_DiskW", "Test_DiskW", "0");
        Add("Test_DiskR", "Test_DiskR", "0");
        HasResults = Results.Count > 0;
    }

    private static string Pct(float? v) => v is null ? "—" : $"{v.Value:0}%";

    private static string Temp(float? v) => v is null ? "—" : $"{v.Value:0} °C";

    private void OnUpdated(TelemetrySample s)
    {
        var t = TelemetryService.Instance;

        Cpu.Percent = s.CpuLoad ?? 0;
        Cpu.PercentText = Pct(s.CpuLoad);
        Cpu.TempValue = s.CpuTemp ?? double.NaN;
        Cpu.TempText = Temp(s.CpuTemp);
        Cpu.Line1 = s.CpuClock is { } cc ? $"{cc / 1000:0.00} GHz" : "";
        Cpu.Line2 = s.CpuPower is { } cp ? $"{cp:0} W" : "";
        Cpu.History = t.History("cpuLoad");

        Gpu.Percent = s.GpuLoad ?? 0;
        Gpu.PercentText = Pct(s.GpuLoad);
        Gpu.TempValue = s.GpuTemp ?? double.NaN;
        Gpu.TempText = Temp(s.GpuTemp);
        Gpu.Line1 = s.GpuClock is { } gc ? $"{gc:0} MHz" : "";
        Gpu.Line2 = s.GpuPower is { } gp ? $"{gp:0} W" : "";
        Gpu.History = t.History("gpuLoad");

        Ram.Percent = s.RamLoad ?? 0;
        Ram.PercentText = Pct(s.RamLoad);
        Ram.Line1 = s.RamUsedGb is { } u && s.RamTotalGb is { } tot ? $"{u:0.0} / {tot:0.0} GB" : "";
        Ram.Line2 = "";
        Ram.History = t.History("ramLoad");

        Disk.Percent = s.DiskUsed ?? 0;
        Disk.PercentText = Pct(s.DiskUsed);
        Disk.TempValue = s.DiskTemp ?? double.NaN;
        Disk.TempText = Temp(s.DiskTemp);
        Disk.Line1 = s.DiskUsedGb is { } du && s.DiskTotalGb is { } dt ? $"{du:0} / {dt:0} GB" : "";
        Disk.Line2 = "";
        Disk.History = t.History("diskTemp");

        CpuLoadHistory = Cpu.History;
        GpuLoadHistory = Gpu.History;
        TempAvailable = s.CpuTemp is not null;
    }

    [RelayCommand]
    private void Go(string target) => _navigate(target);

    public OverlayService OverlaySettings => OverlayService.Instance;

    [RelayCommand]
    private static void ToggleOverlay() => OverlayService.Instance.Toggle();

    public override void Dispose()
    {
        TelemetryService.Instance.Updated -= OnUpdated;
        _processTimer.Stop();
    }
}



