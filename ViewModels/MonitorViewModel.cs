using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Hardware;
using Otimizador.App.Hardware;
using Otimizador.App.Network;
using Otimizador.App.Alerts;
using Otimizador.App.Overlay;

namespace Otimizador.App.ViewModels;

public partial class SensorRow : ObservableObject
{
    public required string Id { get; init; }
    public required string Hardware { get; init; }
    public required string SensorName { get; init; }
    public required SensorType Type { get; init; }
    public required string Unit { get; init; }

    [ObservableProperty] private string _now = "";
    [ObservableProperty] private string _min = "";
    [ObservableProperty] private string _max = "";
    [ObservableProperty] private double _fraction;

    private float _minV = float.MaxValue, _maxV = float.MinValue;

    public void Update(float v)
    {
        _minV = Math.Min(_minV, v);
        _maxV = Math.Max(_maxV, v);
        Now = $"{v:0.#} {Unit}";
        Min = $"{_minV:0.#}";
        Max = $"{_maxV:0.#}";
        double scale = Type switch
        {
            SensorType.Load => 100,
            SensorType.Temperature => 110,
            _ => Math.Max(1, _maxV),
        };
        Fraction = Math.Clamp(v / scale * 100, 0, 100);
    }
}

public sealed record FilterOption(LocText Title, SensorType? Type);

public partial class CoreBar : ObservableObject
{
    public required string Label { get; init; }

    [ObservableProperty] private double _percent;
    [ObservableProperty] private double _barHeight;
    [ObservableProperty] private string _text = "";

    public void Update(double pct)
    {
        Percent = pct;
        BarHeight = Math.Clamp(pct, 0, 100) / 100 * 72;
        Text = $"{pct:0}%";
    }
}

public partial class MonitorViewModel : PageViewModel
{
    private readonly Dictionary<string, SensorRow> _rows = new();
    private StreamWriter? _log;

    public override string TitleKey => "Nav_Monitor";

    public override string SubtitleKey => "Sub_Monitor";

    public OverlayService OverlaySettings => OverlayService.Instance;

    public AlertService AlertSettings => AlertService.Instance;

    public ObservableCollection<SensorRow> Rows { get; } = new();

    public ObservableCollection<CoreBar> Cores { get; } = new();

    // Rede
    private readonly NetworkMonitor _net = new();
    private readonly List<double> _downMbps = new(), _upMbps = new(), _pingMs = new();
    private int _netTick;
    private bool _netBusy;

    public ObservableCollection<ProcessConnections> Connections { get; } = new();

    [ObservableProperty] private double[] _downHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _upHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _pingHistory = Array.Empty<double>();
    [ObservableProperty] private string _downText = "—";
    [ObservableProperty] private string _upText = "—";
    [ObservableProperty] private string _pingText = "—";
    [ObservableProperty] private double _netMax = 10;

    private readonly Dictionary<int, CoreBar> _cores = new();

    public ICollectionView View { get; }

    public FilterOption[] Filters { get; } =
    {
        new(new LocText("Flt_All"), null),
        new(new LocText("Flt_Temp"), SensorType.Temperature),
        new(new LocText("Flt_Load"), SensorType.Load),
        new(new LocText("Flt_Clock"), SensorType.Clock),
        new(new LocText("Flt_Power"), SensorType.Power),
        new(new LocText("Flt_Fan"), SensorType.Fan),
    };

    [ObservableProperty] private FilterOption _selectedFilter;

    [ObservableProperty] private double[] _cpuLoad = Array.Empty<double>();
    [ObservableProperty] private double[] _cpuTemp = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuLoad = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuTemp = Array.Empty<double>();
    [ObservableProperty] private double[] _ramLoad = Array.Empty<double>();

    [ObservableProperty] private string _cpuLoadText = "—";
    [ObservableProperty] private string _cpuTempText = "—";
    [ObservableProperty] private string _gpuLoadText = "—";
    [ObservableProperty] private string _gpuTempText = "—";
    [ObservableProperty] private string _ramText = "—";
    [ObservableProperty] private string _ramDetail = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _logPath = "";
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private int _sensorCount;

    public MonitorViewModel()
    {
        _selectedFilter = Filters[0];
        View = CollectionViewSource.GetDefaultView(Rows);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorRow.Hardware)));
        View.Filter = o => o is SensorRow r && (SelectedFilter.Type is null || r.Type == SelectedFilter.Type);

        TelemetryService.Instance.Updated += OnUpdated;
        if (TelemetryService.Instance.Latest is { } s) OnUpdated(s);
    }

    partial void OnSelectedFilterChanged(FilterOption value) => View.Refresh();

    partial void OnIsRecordingChanged(bool value)
    {
        if (value)
        {
            var dir = Path.Combine(AppServices.DataDir, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"monitor-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _log = new StreamWriter(path, false, Encoding.UTF8);
            _log.WriteLine("time,hardware,sensor,type,value");
            LogPath = Loc.T("Mon_LogAt") + path;
        }
        else
        {
            CloseLog();
            LogPath = "";
        }
    }

    private static void Push(List<double> list, double v)
    {
        list.Add(v);
        if (list.Count > 120) list.RemoveAt(0);
    }

    private async Task NetworkTickAsync()
    {
        if (_netBusy) return;
        _netBusy = true;
        try
        {
            var n = await _net.SampleAsync();
            Push(_downMbps, n.DownBytesPerSec * 8 / 1e6);
            Push(_upMbps, n.UpBytesPerSec * 8 / 1e6);
            Push(_pingMs, n.PingMs ?? double.NaN);
            DownHistory = _downMbps.ToArray();
            UpHistory = _upMbps.ToArray();
            PingHistory = _pingMs.ToArray();
            DownText = NetworkMonitor.FormatSpeed(n.DownBytesPerSec);
            UpText = NetworkMonitor.FormatSpeed(n.UpBytesPerSec);
            PingText = n.PingMs is { } p ? $"{p:0} ms" : "—";
            double peak = Math.Max(_downMbps.DefaultIfEmpty(0).Max(), _upMbps.DefaultIfEmpty(0).Max());
            NetMax = Math.Max(10, Math.Ceiling(peak * 1.25 / 10) * 10);

            if (_netTick++ % 5 == 0)
            {
                var top = await Task.Run(() => NetworkMonitor.TopConnections());
                Connections.Clear();
                foreach (var c in top) Connections.Add(c);
            }
        }
        catch
        {
            // sem rede: mostra traços
        }
        finally
        {
            _netBusy = false;
        }
    }

    private static string Fmt(float? v, string unit) => v is null ? "—" : $"{v.Value:0}{unit}";

    private void OnUpdated(TelemetrySample s)
    {
        _ = NetworkTickAsync();
        var t = TelemetryService.Instance;
        CpuLoad = t.History("cpuLoad");
        CpuTemp = t.History("cpuTemp");
        GpuLoad = t.History("gpuLoad");
        GpuTemp = t.History("gpuTemp");
        RamLoad = t.History("ramLoad");

        CpuLoadText = Fmt(s.CpuLoad, "%");
        CpuTempText = Fmt(s.CpuTemp, " °C");
        GpuLoadText = Fmt(s.GpuLoad, "%");
        GpuTempText = Fmt(s.GpuTemp, " °C");
        RamText = Fmt(s.RamLoad, "%");
        RamDetail = s.RamUsedGb is { } u && s.RamTotalGb is { } tot ? $"{u:0.0} / {tot:0.0} GB" : "";

        Hint = s.CpuTemp is not null ? ""
            : AppServices.IsAdmin && !SensorService.PawnIoInstalled() ? Loc.T("Mon_NoDriver")
            : AppServices.IsAdmin ? Loc.T("Mon_NoTemp")
            : Loc.T("Mon_NeedAdmin");

        foreach (var r in s.Raw.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Load))
        {
            var m = System.Text.RegularExpressions.Regex.Match(r.Name, @"^CPU Core #(\d+)$");
            if (!m.Success) continue;
            int n = int.Parse(m.Groups[1].Value);
            if (!_cores.TryGetValue(n, out var core))
            {
                core = new CoreBar { Label = n.ToString() };
                _cores[n] = core;
                Cores.Add(core);
            }
            core.Update(r.Value);
        }

        foreach (var r in s.Raw)
        {
            if (!_rows.TryGetValue(r.Id, out var row))
            {
                row = new SensorRow
                {
                    Id = r.Id, Hardware = r.Hardware, SensorName = r.Name, Type = r.Type, Unit = SensorService.Unit(r.Type),
                };
                _rows[r.Id] = row;
                Rows.Add(row);
            }
            row.Update(r.Value);
        }
        SensorCount = Rows.Count;

        if (_log is not null)
        {
            var stamp = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            foreach (var r in s.Raw)
                _log.WriteLine(FormattableString.Invariant($"{stamp},\"{r.Hardware}\",\"{r.Name}\",{r.Type},{r.Value}"));
            _log.Flush();
        }
    }

    private void CloseLog()
    {
        _log?.Dispose();
        _log = null;
    }

    public override void Dispose()
    {
        TelemetryService.Instance.Updated -= OnUpdated;
        CloseLog();
    }
}




