using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Otimizador.App.Dialogs;
using Otimizador.App.Disk;
using Otimizador.App.Hardware;

namespace Otimizador.App.ViewModels;

public sealed record SectionItem(string Key, string Title, string Glyph, IReadOnlyList<InfoLine> Lines);

public sealed class BatteryItem
{
    public required BatteryHealth Battery { get; init; }
    public string Health => Battery.HealthPercent is { } h ? $"{h:0}%" : "—";
    public string Design => Battery.DesignedMWh is { } d ? $"{d / 1000:0.0} Wh" : "—";
    public string Full => Battery.FullChargeMWh is { } f ? $"{f / 1000:0.0} Wh" : "—";
    public string Cycles => Battery.Cycles is { } c ? c.ToString() : "—";
    public string Charge => Battery.ChargePercent is { } p ? $"{p}%" : "—";
    public string Verdict => Battery.Verdict;
    public int Level => (int)Battery.Level;
}

public sealed class DiskHealthItem
{
    public required DiskHealth Disk { get; init; }
    public string Name => Disk.Name;
    public string Subtitle => $"{Disk.Kind} · {SizeFormat.Bytes(Disk.SizeBytes)}";
    public string Verdict => Disk.Verdict;
    public int Level => (int)Disk.Level;
    public string Temperature => Disk.TemperatureC is { } t ? $"{t:0} °C" : "—";
    public string Wear => Disk.WearPercent is { } w ? $"{w:0}%" : "—";
    public string Hours => Disk.PowerOnHours is { } h ? $"{h:N0} h" : "—";
    public string Errors => Disk.ReadErrors is null && Disk.WriteErrors is null ? "—" : $"{(Disk.ReadErrors ?? 0) + (Disk.WriteErrors ?? 0)}";
    public string Details => Disk.Details;
    public bool HasDetails => !string.IsNullOrEmpty(Disk.Details);
}

public partial class HardwareViewModel : PageViewModel
{
    private List<InfoSection> _sections = new();

    public override string TitleKey => "Nav_Hardware";

    public override string SubtitleKey => "Sub_Hardware";

    /// <summary>Duas colunas independentes (cartões com alturas diferentes não se esticam entre si).</summary>
    public ObservableCollection<SectionItem> LeftSections { get; } = new();

    public ObservableCollection<SectionItem> RightSections { get; } = new();

    public ObservableCollection<DiskHealthItem> Disks { get; } = new();

    [ObservableProperty] private bool _hasDisks;
    [ObservableProperty] private BatteryItem? _battery;
    [ObservableProperty] private bool _hasBattery;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private string _ramTotal = "";
    [ObservableProperty] private string _osName = "";

    private static string GlyphFor(string key) => key switch
    {
        "system" => "",
        "cpu" => "",
        "memory" => "",
        "gpu" => "",
        "board" => "",
        "disks" => "",
        "battery" => "",
        _ => "",
    };

    public HardwareViewModel()
    {
        Loc.Instance.PropertyChanged += OnLocChanged;
        _ = LoadAsync(force: false);
    }

    // As seções guardam texto traduzido: relê quando o idioma muda.
    private void OnLocChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Loc.Lang)) _ = LoadAsync(force: true);
    }

    private async Task LoadAsync(bool force)
    {
        IsLoading = true;
        Status = Loc.T("Hw_Loading");
        _sections = await HardwareInfoService.GetAsync(force);
        LeftSections.Clear();
        RightSections.Clear();
        for (int i = 0; i < _sections.Count; i++)
        {
            var s = _sections[i];
            (i % 2 == 0 ? LeftSections : RightSections).Add(new SectionItem(s.Key, s.Title, GlyphFor(s.Key), s.Lines));
        }
        CpuName = HardwareInfoService.Headline(_sections, "cpu") ?? "";
        GpuName = HardwareInfoService.Headline(_sections, "gpu") ?? "";
        RamTotal = HardwareInfoService.Headline(_sections, "memory") ?? "";
        OsName = HardwareInfoService.Headline(_sections, "system") ?? "";
        var disks = await Task.Run(DiskHealthService.Read);
        Disks.Clear();
        foreach (var d in disks) Disks.Add(new DiskHealthItem { Disk = d });
        HasDisks = Disks.Count > 0;
        var bat = await Task.Run(BatteryHealthService.Read);
        Battery = bat is null ? null : new BatteryItem { Battery = bat };
        HasBattery = bat is not null;
        Status = "";
        IsLoading = false;
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(force: true);

    [RelayCommand]
    private void Export()
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"meu-pc-{DateTime.Now:yyyyMMdd}",
            Filter = "Texto (*.txt)|*.txt|JSON (*.json)|*.json",
        };
        if (dlg.ShowDialog() != true) return;
        bool json = dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(dlg.FileName, json ? ExportJson() : ExportText(), System.Text.Encoding.UTF8);
        Status = Loc.T("Hw_Exported") + dlg.FileName;
    }

    private string DiskLine(DiskHealthItem d) =>
        $"{d.Name} ({d.Subtitle}): {d.Verdict}; temp {d.Temperature}, desgaste {d.Wear}, horas {d.Hours}, erros {d.Errors}";

    private string ExportText()
    {
        var text = HardwareInfoService.ToText(_sections);
        if (Disks.Count > 0) text += $"{Environment.NewLine}[Saúde dos discos]{Environment.NewLine}" + string.Join(Environment.NewLine, Disks.Select(DiskLine));
        if (Battery is { } b) text += $"{Environment.NewLine}[Bateria]{Environment.NewLine}Saúde {b.Health}, ciclos {b.Cycles}, projetada {b.Design}, carga máx. {b.Full}: {b.Verdict}";
        return text;
    }

    /// <summary>Mesmo conteúdo em JSON, bom para comparar PCs ou guardar o inventário da empresa.</summary>
    private string ExportJson()
    {
        var doc = new
        {
            machine = Environment.MachineName,
            date = DateTime.Now.ToString("s"),
            app = AppInfo.Version.ToString(3),
            sections = _sections.Select(s => new
            {
                key = s.Key,
                title = s.Title,
                lines = s.Lines.Select(l => new { label = l.Label, value = l.Value }),
            }),
            disks = Disks.Select(d => new { d.Name, d.Subtitle, d.Verdict, d.Temperature, d.Wear, d.Hours, d.Errors }),
            battery = Battery is { } b ? new { b.Health, b.Cycles, b.Design, b.Full, b.Verdict } : null,
        };
        return System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    public override void Dispose() => Loc.Instance.PropertyChanged -= OnLocChanged;
}



