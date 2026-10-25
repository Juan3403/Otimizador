using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Boot;

namespace Otimizador.App.ViewModels;

public partial class StartupItem : ObservableObject
{
    private bool _suppress;

    public StartupEntry Entry { get; }

    public Action<StartupItem>? Toggled { get; set; }

    [ObservableProperty] private bool _isEnabled;

    public StartupItem(StartupEntry entry)
    {
        Entry = entry;
        _isEnabled = entry.Enabled;
    }

    public string Name => Entry.Name;
    public string Subtitle => string.Join("  ·  ", new[] { Entry.Description, Entry.Publisher }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Command => Entry.Command;
    public string Source => Entry.Source;
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_suppress) Toggled?.Invoke(this);
    }

    /// <summary>Volta o interruptor sem disparar a gravação de novo (usado quando a gravação falha).</summary>
    public void Revert(bool value)
    {
        _suppress = true;
        IsEnabled = value;
        _suppress = false;
    }
}

public partial class StartupViewModel : PageViewModel
{
    public override string TitleKey => "Nav_Startup";

    public override string SubtitleKey => "Sub_Startup";

    public ObservableCollection<StartupItem> Items { get; } = new();

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isLoading = true;

    public StartupViewModel() => _ = ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        IsLoading = true;
        var entries = await Task.Run(StartupService.List);
        Items.Clear();
        foreach (var e in entries)
        {
            var item = new StartupItem(e) { Toggled = OnToggled };
            Items.Add(item);
        }
        UpdateSummary();
        IsLoading = false;
    }

    private void OnToggled(StartupItem item)
    {
        bool want = item.IsEnabled;
        if (StartupService.SetEnabled(item.Entry, want))
        {
            Status = $"{item.Name}: {Loc.T(want ? "Boot_NowOn" : "Boot_NowOff")}";
            EventLog.Add("startup", $"{item.Name} {(want ? "ligado" : "desligado")}");
        }
        else
        {
            item.Revert(!want);
            Status = $"{item.Name}: {Loc.T("Boot_Failed")}";
        }
        UpdateSummary();
    }

    private void UpdateSummary() =>
        Summary = $"{Items.Count(i => i.IsEnabled)} {Loc.T("Boot_Active")} · {Items.Count} {Loc.T("Boot_Total")}";
}
