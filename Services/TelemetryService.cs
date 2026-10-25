using Otimizador.App.Hardware;

namespace Otimizador.App;

public sealed record TelemetrySample(
    float? CpuLoad, float? CpuTemp, float? CpuClock, float? CpuPower,
    float? GpuLoad, float? GpuTemp, float? GpuClock, float? GpuPower,
    float? RamLoad, float? RamUsedGb, float? RamTotalGb,
    float? DiskUsed, float? DiskTemp, float? DiskUsedGb, float? DiskTotalGb,
    IReadOnlyList<SensorReading> Raw);

/// <summary>
/// Lê os sensores a cada segundo, guarda o histórico recente e avisa as telas na thread da interface.
/// Pode ser pausado (benchmarks) para não competir com os testes.
/// </summary>
public sealed class TelemetryService
{
    public static TelemetryService Instance { get; } = new();

    public const int Capacity = 120;

    private readonly Dictionary<string, List<double>> _history = new();
    private CancellationTokenSource? _cts;
    private SynchronizationContext? _ui;

    public event Action<TelemetrySample>? Updated;

    public TelemetrySample? Latest { get; private set; }

    public volatile bool Paused;

    public void Start()
    {
        if (_cts is not null) return;
        _ui = SynchronizationContext.Current;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                if (!Paused)
                {
                    try
                    {
                        var sample = Build(AppServices.Sensors.Snapshot());
                        _ui?.Post(_ => Publish(sample), null);
                    }
                    catch
                    {
                        // uma leitura ruim não derruba o monitor
                    }
                }
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, ct);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    /// <summary>Histórico (cópia) de uma série: cpuLoad, cpuTemp, gpuLoad, gpuTemp, ramLoad, cpuClock, cpuPower, gpuPower, diskTemp.</summary>
    public double[] History(string key) =>
        _history.TryGetValue(key, out var l) ? l.ToArray() : Array.Empty<double>();

    /// <summary>Uso do disco do Windows (C:) direto do sistema: não depende de sensor nem de administrador.</summary>
    private static (float? Pct, float? UsedGb, float? TotalGb) SystemDrive()
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            float total = d.TotalSize / 1073741824f;
            float used = total - d.AvailableFreeSpace / 1073741824f;
            return (used / total * 100, used, total);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static TelemetrySample Build(List<SensorReading> s)
    {
        var drive = SystemDrive();
        return new(
            SensorService.CpuLoad(s), SensorService.CpuTemp(s), SensorService.CpuClock(s), SensorService.CpuPower(s),
            SensorService.GpuLoad(s), SensorService.GpuTemp(s), SensorService.GpuClock(s), SensorService.GpuPower(s),
            SensorService.RamLoad(s), SensorService.RamUsedGb(s), SensorService.RamTotalGb(s),
            drive.Pct ?? SensorService.DiskUsed(s), SensorService.DiskTemp(s), drive.UsedGb, drive.TotalGb, s);
    }

    private void Add(string key, float? v)
    {
        if (!_history.TryGetValue(key, out var list)) _history[key] = list = new List<double>(Capacity + 1);
        list.Add(v is null ? double.NaN : v.Value);
        if (list.Count > Capacity) list.RemoveAt(0);
    }

    private void Publish(TelemetrySample s)
    {
        Latest = s;
        Add("cpuLoad", s.CpuLoad);
        Add("cpuTemp", s.CpuTemp);
        Add("cpuClock", s.CpuClock);
        Add("cpuPower", s.CpuPower);
        Add("gpuLoad", s.GpuLoad);
        Add("gpuTemp", s.GpuTemp);
        Add("gpuClock", s.GpuClock);
        Add("gpuPower", s.GpuPower);
        Add("ramLoad", s.RamLoad);
        Add("diskTemp", s.DiskTemp);
        Updated?.Invoke(s);
    }
}

