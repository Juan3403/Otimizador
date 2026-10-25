using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Otimizador.App.ViewModels;

public sealed record RangeOption(LocText Title, TimeSpan Range, int Buckets);

public sealed record EventRow(string Time, string Kind, string Text);

public partial class HistoryViewModel : PageViewModel
{
    public override string TitleKey => "Nav_History";

    public override string SubtitleKey => "Sub_History";

    public RangeOption[] Ranges { get; } =
    {
        new(new LocText("Hist_1h"), TimeSpan.FromHours(1), 60),
        new(new LocText("Hist_24h"), TimeSpan.FromHours(24), 240),
        new(new LocText("Hist_7d"), TimeSpan.FromDays(7), 240),
    };

    public ObservableCollection<EventRow> Events { get; } = new();

    [ObservableProperty] private RangeOption _selectedRange;
    [ObservableProperty] private double[] _cpuLoad = Array.Empty<double>();
    [ObservableProperty] private double[] _cpuTemp = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuLoad = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuTemp = Array.Empty<double>();
    [ObservableProperty] private double[] _ram = Array.Empty<double>();
    [ObservableProperty] private int _capacity = 60;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _maxCpuTemp = "—";
    [ObservableProperty] private string _maxGpuTemp = "—";
    [ObservableProperty] private string _avgCpuLoad = "—";
    [ObservableProperty] private string _avgRam = "—";
    [ObservableProperty] private string _rangeStart = "";
    [ObservableProperty] private string _reportStatus = "";
    [ObservableProperty] private bool _isBusy;

    public HistoryViewModel()
    {
        _selectedRange = Ranges[1];
        _ = ReloadAsync();
    }

    partial void OnSelectedRangeChanged(RangeOption value) => _ = ReloadAsync();

    private static string Max(IEnumerable<double> v, string unit)
    {
        var l = v.Where(x => !double.IsNaN(x)).ToList();
        return l.Count == 0 ? "—" : $"{l.Max():0}{unit}";
    }

    private static string Avg(IEnumerable<double> v, string unit)
    {
        var l = v.Where(x => !double.IsNaN(x)).ToList();
        return l.Count == 0 ? "—" : $"{l.Average():0}{unit}";
    }

    private async Task ReloadAsync()
    {
        var range = SelectedRange;
        HistoryService.Instance.Flush(); // inclui o minuto em andamento
        var (points, events) = await Task.Run(() =>
            (HistoryService.Instance.Read(range.Range), EventLog.Read(DateTime.Now - range.Range, 80)));
        if (!ReferenceEquals(range, SelectedRange)) return; // o usuário já trocou o período

        HasData = points.Count >= 2;
        Capacity = range.Buckets;
        CpuLoad = HistoryService.Bucket(points, range.Range, range.Buckets, p => p.CpuLoad, false);
        CpuTemp = HistoryService.Bucket(points, range.Range, range.Buckets, p => p.CpuTemp, true);
        GpuLoad = HistoryService.Bucket(points, range.Range, range.Buckets, p => p.GpuLoad, false);
        GpuTemp = HistoryService.Bucket(points, range.Range, range.Buckets, p => p.GpuTemp, true);
        Ram = HistoryService.Bucket(points, range.Range, range.Buckets, p => p.Ram, false);

        MaxCpuTemp = Max(points.Select(p => p.CpuTemp), " °C");
        MaxGpuTemp = Max(points.Select(p => p.GpuTemp), " °C");
        AvgCpuLoad = Avg(points.Select(p => p.CpuLoad), "%");
        AvgRam = Avg(points.Select(p => p.Ram), "%");
        RangeStart = (DateTime.Now - range.Range).ToString(range.Range.TotalDays >= 1 ? "g" : "t");

        Events.Clear();
        foreach (var e in events) Events.Add(new EventRow(e.Time.ToString("g"), e.Kind, e.Text));
    }

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"relatorio-otimizador-{DateTime.Now:yyyyMMdd-HHmm}.html",
            Filter = "HTML (*.html)|*.html",
        };
        if (dlg.ShowDialog() != true) return;
        IsBusy = true;
        ReportStatus = Loc.T("Hist_Generating");
        try
        {
            var html = await ReportService.BuildHtmlAsync();
            await File.WriteAllTextAsync(dlg.FileName, html);
            ReportStatus = Loc.T("Hw_Exported") + dlg.FileName;
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportStatus = $"{Loc.T("Opt_Error")}: {ex.Message}";
        }
        IsBusy = false;
    }
}
