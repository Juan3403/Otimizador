using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Bench;

namespace Otimizador.App.ViewModels;

public sealed record DurationOption(string Label, TimeSpan Value);

public partial class ScoreTile : ObservableObject
{
    public required LocText Title { get; init; }
    public required string Value { get; init; }
    public required string Unit { get; init; }
    public string PreviousText { get; init; } = "";
    public string DeltaText { get; init; } = "";

    /// <summary>1 = melhor que antes, -1 = pior, 0 = igual/sem comparação.</summary>
    public int DeltaKind { get; init; }
}

public partial class TestViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;
    private readonly List<double> _tempSeries = new();
    private readonly List<double> _loadSeries = new();

    public override string TitleKey => "Nav_Test";

    public override string SubtitleKey => "Sub_Test";

    public ObservableCollection<ScoreTile> Tiles { get; } = new();

    public DurationOption[] Durations { get; } =
    {
        new("30 s", TimeSpan.FromSeconds(30)),
        new("2 min", TimeSpan.FromMinutes(2)),
        new("10 min", TimeSpan.FromMinutes(10)),
    };

    [ObservableProperty] private DurationOption _selectedDuration;

    // Peças escolhidas (valem para o teste rápido e para o de estabilidade)
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasSelection))][NotifyCanExecuteChangedFor(nameof(QuickTestCommand), nameof(StressCommand))] private bool _useCpu = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasSelection))][NotifyCanExecuteChangedFor(nameof(QuickTestCommand), nameof(StressCommand))] private bool _useRam = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasSelection))][NotifyCanExecuteChangedFor(nameof(QuickTestCommand), nameof(StressCommand))] private bool _useGpu = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasSelection))][NotifyCanExecuteChangedFor(nameof(QuickTestCommand), nameof(StressCommand))] private bool _useDisk = true;

    public bool HasSelection => UseCpu || UseRam || UseGpu || UseDisk;

    [ObservableProperty] private string _sensorHint = "";
    [ObservableProperty] private string _gpuTempText = "—";
    [ObservableProperty] private string _maxGpuTempText = "—";
    [ObservableProperty] private double[] _gpuTempChart = Array.Empty<double>();
    private readonly List<double> _gpuTempSeries = new();

    private StressTargets Targets =>
        (UseCpu ? StressTargets.Cpu : 0) | (UseRam ? StressTargets.Memory : 0) |
        (UseGpu ? StressTargets.Gpu : 0) | (UseDisk ? StressTargets.Disk : 0);

    private static string Names(StressTargets t)
    {
        var parts = new List<string>();
        if (t.HasFlag(StressTargets.Cpu)) parts.Add("CPU");
        if (t.HasFlag(StressTargets.Memory)) parts.Add("RAM");
        if (t.HasFlag(StressTargets.Gpu)) parts.Add("GPU");
        if (t.HasFlag(StressTargets.Disk)) parts.Add(Loc.T("Test_DiskName"));
        return string.Join(", ", parts);
    }

    // Teste rápido
    [ObservableProperty] private double _quickProgress;
    [ObservableProperty] private string _quickStatus = "";

    // Estabilidade
    [ObservableProperty] private double _stressProgress;
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _errorsText = "0";
    [ObservableProperty] private string _tempText = "—";
    [ObservableProperty] private string _maxTempText = "—";
    [ObservableProperty] private string _iterationsText = "0";
    [ObservableProperty] private double[] _tempChart = Array.Empty<double>();
    [ObservableProperty] private double[] _loadChart = Array.Empty<double>();
    [ObservableProperty] private string _resultText = "";

    /// <summary>0 = sem resultado, 1 = aprovado, 2 = falhou, 3 = interrompido/cancelado.</summary>
    [ObservableProperty] private int _resultKind;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuickTestCommand), nameof(StressCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty] private bool _quickRunning;
    [ObservableProperty] private bool _stressRunning;

    public bool IsIdle => !IsBusy;

    public TestViewModel() => _selectedDuration = Durations[0];

    private bool CanStart() => !IsBusy && HasSelection;

    private void AddTile(BenchResult r, Dictionary<string, double> previous)
    {
        string fmt = r.Unit == "GB/s" ? "0.0" : "0";
        string prevText = "", deltaText = "";
        int kind = 0;
        if (previous.TryGetValue(r.Key, out var p) && p > 0)
        {
            prevText = $"{Loc.T("Test_Previous")}: {p.ToString(fmt)}";
            double pct = (r.Value - p) / p * 100;
            kind = Math.Abs(pct) < 0.5 ? 0 : pct > 0 ? 1 : -1;
            deltaText = $"{(pct >= 0 ? "+" : "")}{pct:0.0}%";
        }
        Tiles.Add(new ScoreTile
        {
            Title = new LocText(r.Key), Value = r.Value.ToString(fmt), Unit = r.Unit,
            PreviousText = prevText, DeltaText = deltaText, DeltaKind = kind,
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task QuickTestAsync()
    {
        Begin();
        QuickRunning = true;
        Tiles.Clear();
        QuickProgress = 0;
        var ct = _cts!.Token;
        try
        {
            var previous = BenchHistory.Load();
            var results = new List<BenchResult>();
            int steps = (UseCpu ? 2 : 0) + (UseRam ? 1 : 0) + (UseDisk ? 1 : 0) + (UseGpu ? 1 : 0);
            int done = 0;
            void Step() => QuickProgress = ++done * 100.0 / steps;

            if (UseCpu)
            {
                QuickStatus = Loc.T("Test_CpuSingle");
                var r1 = await Task.Run(() => Benchmarks.CpuSingle(TimeSpan.FromSeconds(4), ct));
                ct.ThrowIfCancellationRequested();
                results.Add(r1); AddTile(r1, previous); Step();

                QuickStatus = Loc.T("Test_CpuMulti");
                var r2 = await Task.Run(() => Benchmarks.CpuMulti(TimeSpan.FromSeconds(4), ct));
                ct.ThrowIfCancellationRequested();
                results.Add(r2); AddTile(r2, previous); Step();
            }

            if (UseRam)
            {
                QuickStatus = Loc.T("Test_Ram");
                var r3 = await Task.Run(() => Benchmarks.Memory(ct));
                ct.ThrowIfCancellationRequested();
                results.Add(r3); AddTile(r3, previous); Step();
            }

            if (UseDisk)
            {
                QuickStatus = Loc.T("Test_DiskW");
                var (w, r) = await Task.Run(() => Benchmarks.Disk(ct));
                ct.ThrowIfCancellationRequested();
                results.Add(w); AddTile(w, previous);
                results.Add(r); AddTile(r, previous);
                Step();
            }

            if (UseGpu)
            {
                QuickStatus = Loc.T("Test_Gpu");
                try
                {
                var gpu = await Task.Run(() => GpuBenchmark.Run(TimeSpan.FromSeconds(2.5), ct));
                ct.ThrowIfCancellationRequested();
                if (gpu is not null)
                {
                    results.Add(gpu.Compute); AddTile(gpu.Compute, previous);
                    results.Add(gpu.Memory); AddTile(gpu.Memory, previous);
                }
                else
                {
                    QuickStatus = Loc.T("Test_NoGpu");
                }
            }
                catch (InvalidOperationException ex)
                {
                    Tiles.Add(new ScoreTile { Title = new LocText("Test_Gpu"), Value = "ERRO", Unit = "", PreviousText = ex.Message });
                }
                Step();
            }
            QuickProgress = 100;

            BenchHistory.Save(results);
            if (results.Count > 0)
                EventLog.Add("test", "Teste rápido: " + string.Join("; ", results.Select(r => $"{Loc.T(r.Key)} {r.Value:0.#} {r.Unit}")));
            QuickStatus = Loc.T("Test_Done");
        }
        catch (OperationCanceledException)
        {
            QuickStatus = Loc.T("Test_Cancelled");
        }
        finally
        {
            QuickRunning = false;
            End();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StressAsync()
    {
        Begin();
        StressRunning = true;
        ResultKind = 0;
        ResultText = "";
        StressProgress = 0;
        _tempSeries.Clear();
        _loadSeries.Clear();
        _gpuTempSeries.Clear();
        SensorHint = "";
        TempChart = LoadChart = GpuTempChart = Array.Empty<double>();
        MaxTempText = TempText = GpuTempText = MaxGpuTempText = "—";
        ErrorsText = "0";

        var ct = _cts!.Token;
        var duration = SelectedDuration.Value;
        var targets = Targets;
        var gpuMissing = false;
        var failed = StressTargets.None;
        var progress = new Progress<StressStatus>(s =>
        {
            ElapsedText = $"{(int)s.Elapsed.TotalMinutes:00}:{s.Elapsed.Seconds:00}";
            ErrorsText = s.Errors.ToString();
            IterationsText = s.Iterations.ToString("N0");
            StressProgress = Math.Min(100, s.Elapsed.TotalSeconds / duration.TotalSeconds * 100);
            if (s.MaxTemp is { } m) MaxTempText = $"{m:0} °C";
            if (s.MaxGpuTemp is { } gm) MaxGpuTempText = $"{gm:0} °C";
            failed = s.FailedTargets;
            gpuMissing = s.GpuMissing;
            if (s.Final) return;
            if (SensorHint.Length == 0 && s.Elapsed.TotalSeconds >= 2)
            {
                // explica por que a temperatura aparece como "—"
                if (targets.HasFlag(StressTargets.Cpu) && s.CpuTemp is null)
                    SensorHint = !AppServices.IsAdmin ? Loc.T("Test_HintCpuAdmin")
                        : !Hardware.SensorService.PawnIoInstalled() ? Loc.T("Test_HintCpuDriver") : Loc.T("Test_HintCpuNone");
                else if (targets.HasFlag(StressTargets.Gpu) && s.GpuTemp is null && !gpuMissing)
                    SensorHint = Loc.T("Test_HintGpuNone");
            }
            TempText = s.CpuTemp is { } t ? $"{t:0} °C" : "—";
            GpuTempText = s.GpuTemp is { } g ? $"{g:0} °C" : "—";
            _tempSeries.Add(s.CpuTemp ?? double.NaN);
            _loadSeries.Add(s.CpuLoad ?? double.NaN);
            _gpuTempSeries.Add(s.GpuTemp ?? double.NaN);
            TempChart = _tempSeries.ToArray();
            LoadChart = _loadSeries.ToArray();
            GpuTempChart = _gpuTempSeries.ToArray();
        });
        try
        {
            var outcome = await Task.Run(() => StressTest.Run(duration, AppServices.Sensors, progress, ct, targets));
            (ResultKind, ResultText) = outcome switch
            {
                StressOutcome.Passed => (1, Loc.T("Test_Passed")),
                StressOutcome.Failed => (2, $"{Loc.T("Test_Failed")} [{Names(failed)}]"),
                StressOutcome.Aborted => (3, Loc.T("Test_Aborted")),
                _ => (3, Loc.T("Test_Cancelled")),
            };
            if (gpuMissing) ResultText += " " + Loc.T("Test_GpuSkipped");
            if (outcome != StressOutcome.Cancelled)
            {
                string verdict = outcome switch
                {
                    StressOutcome.Passed => "aprovado, 0 erros",
                    StressOutcome.Failed => $"FALHOU em {Names(failed)}",
                    _ => "interrompido por temperatura alta",
                };
                EventLog.Add("test", $"Estabilidade ({Names(targets)}, {SelectedDuration.Label}): {verdict}; " +
                                     $"temp. máx. CPU {MaxTempText}, GPU {MaxGpuTempText}");
            }
        }
        finally
        {
            StressRunning = false;
            End();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void Begin()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        TelemetryService.Instance.Paused = true; // os testes leem os sensores por conta própria
    }

    private void End()
    {
        IsBusy = false;
        TelemetryService.Instance.Paused = false;
    }

    public override void Dispose()
    {
        _cts?.Cancel();
        TelemetryService.Instance.Paused = false;
    }
}

