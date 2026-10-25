using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Otimizador.App.Inventory;

namespace Otimizador.App.ViewModels;

public sealed class InventoryRow
{
    public required InventoryRecord Record { get; init; }
    public string Machine => Record.Machine;
    public string User => Record.User;
    public string Specs => $"{Record.Cpu}  ·  {Record.RamGb:0} GB RAM  ·  {Record.Gpu}";
    public string Os => $"{Record.Os}  ·  v{Record.AppVersion}";
    public string Disks => string.Join("  ·  ", Record.Disks.Select(d => $"{d.Name} ({d.SizeGb:0} GB, {d.Health})"));
    public string Security => Record.SecurityTotal is > 0 ? $"{Record.SecurityGood}/{Record.SecurityTotal}" : "—";
    public bool IsStale => DateTime.Now - Record.Time > TimeSpan.FromDays(3);
    public string LastSeen => Record.Time.ToString("g");

    /// <summary>0 = tudo bem, 1 = atenção, 2 = problema ou sem atualizar há dias.</summary>
    public int Level => IsStale ? Math.Max(1, Record.Level) : Record.Level;

    public string LevelText => Level switch
    {
        0 => Loc.T("OK", "OK"),
        1 => IsStale ? Loc.T("Sem atualizar", "Not updated") : Loc.T("Atenção", "Attention"),
        _ => Loc.T("Problema", "Problem"),
    };
}

public partial class InventoryViewModel : PageViewModel
{
    public override string TitleKey => "Nav_Inventory";

    public override string SubtitleKey => "Sub_Inventory";

    public ObservableCollection<InventoryRow> Rows { get; } = new();

    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasFolder;

    private bool _loading = true;

    public InventoryViewModel()
    {
        var s = Settings.Load();
        _folder = s.InventoryFolder ?? "";
        _enabled = s.InventoryEnabled;
        _hasFolder = !string.IsNullOrWhiteSpace(_folder);
        _loading = false;
        Reload();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_loading) return;
        var s = Settings.Load();
        s.InventoryEnabled = value;
        s.Save();
        if (value) _ = PublishAsync();
    }

    private void Reload()
    {
        Rows.Clear();
        foreach (var r in InventoryService.ReadAll(Folder)) Rows.Add(new InventoryRow { Record = r });
        int problems = Rows.Count(r => r.Level >= 1);
        Summary = Rows.Count == 0
            ? Loc.T("Nenhum PC publicou nesta pasta ainda.", "No PC has published to this folder yet.")
            : $"{Rows.Count} PCs · {Rows.Count - problems} OK · {problems} {Loc.T("precisam de atenção", "need attention")}";
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Pasta do inventário (pode ser uma pasta de rede)", "Inventory folder (can be a network folder)") };
        if (dlg.ShowDialog() != true) return;
        Folder = dlg.FolderName;
        HasFolder = true;
        var s = Settings.Load();
        s.InventoryFolder = Folder;
        s.Save();
        Reload();
    }

    [RelayCommand]
    private async Task PublishAsync()
    {
        IsBusy = true;
        Status = Loc.T("Publicando...", "Publishing...");
        var (ok, message) = await InventoryService.PublishAsync(Folder);
        Status = ok ? $"{Loc.T("Publicado em", "Published to")} {message}" : $"{Loc.T("Opt_Error")}: {message}";
        Reload();
        IsBusy = false;
    }

    [RelayCommand]
    private void Refresh() => Reload();

    [RelayCommand]
    private void ExportCsv()
    {
        var dlg = new SaveFileDialog { FileName = $"inventario-{DateTime.Now:yyyyMMdd}.csv", Filter = "CSV (*.csv)|*.csv" };
        if (dlg.ShowDialog() != true) return;
        var sb = new StringBuilder("PC;Usuario;Windows;CPU;RAM_GB;GPU;Discos;Inicializacao;Seguranca;Versao;Ultima_atualizacao\n");
        foreach (var r in Rows)
        {
            var c = r.Record;
            sb.AppendLine(string.Join(";", new[]
            {
                c.Machine, c.User, c.Os, c.Cpu, c.RamGb.ToString("0"), c.Gpu, r.Disks, $"{c.StartupEnabled}/{c.StartupTotal}", r.Security, c.AppVersion, c.Time.ToString("s"),
            }.Select(x => x.Replace(";", ","))));
        }
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        Status = Loc.T("Hw_Exported") + dlg.FileName;
    }
}

