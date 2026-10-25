using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Dialogs;
using Otimizador.App.Tweaks;

namespace Otimizador.App.ViewModels;

public sealed record ProfileOption(string Key, LocText Title, LocText Description);

public partial class TweakItem : ObservableObject
{
    public Tweak Tweak { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isApplied;

    public TweakItem(Tweak tweak) => Tweak = tweak;

    public string Name => Tweak.Name;
    public string Description => Tweak.Description;
    public bool IsMedium => Tweak.Risk == Risk.Medium;
    public string RiskText => Loc.T(Tweak.Risk == Risk.Low ? "Opt_Risk_Low" : "Opt_Risk_Medium");

    public string Glyph => Tweak.Id switch
    {
        "adid" or "suggestions" or "bing" or "telemetry" => "",
        "startupdelay" => "",
        "gamemode" or "gamedvr" or "hags" => "",
        "powerplan" => "",
        _ => "",
    };

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(RiskText));
    }
}

public partial class OptimizeViewModel : PageViewModel
{
    private bool _settingProfile;

    public override string TitleKey => "Nav_Optimize";

    public override string SubtitleKey => "Sub_Optimize";

    public ObservableCollection<TweakItem> Items { get; } = new();

    public ProfileOption[] Profiles { get; } =
    {
        new("safe", new LocText("Opt_Safe"), new LocText("Opt_SafeDesc")),
        new("balanced", new LocText("Opt_Balanced"), new LocText("Opt_BalancedDesc")),
        new("gaming", new LocText("Opt_Gaming"), new LocText("Opt_GamingDesc")),
        new("custom", new LocText("Opt_Custom"), new LocText("Opt_CustomDesc")),
    };

    [ObservableProperty] private ProfileOption _selectedProfile;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _status = "";

    /// <summary>0 = neutro, 1 = sucesso, 2 = atenção.</summary>
    [ObservableProperty] private int _statusKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    public OptimizeViewModel(string? preselect = null)
    {
        _selectedProfile = Profiles[3];
        foreach (var t in TweakCatalog.All)
        {
            var item = new TweakItem(t);
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);
        }
        Loc.Instance.PropertyChanged += OnLocChanged;
        if (preselect is not null && Profiles.FirstOrDefault(p => p.Key == preselect) is { } p) SelectedProfile = p;
        _ = RefreshStatesAsync();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TweakItem.IsSelected)) return;
        SelectedCount = Items.Count(i => i.IsSelected);
        if (!_settingProfile) SelectedProfile = Profiles[3]; // marcou à mão → "Personalizado"
    }

    partial void OnSelectedProfileChanged(ProfileOption value)
    {
        if (value.Key == "custom") return;
        _settingProfile = true;
        foreach (var i in Items) i.IsSelected = i.Tweak.Profiles.Contains(value.Key);
        _settingProfile = false;
    }

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var i in Items) i.RefreshLanguage();
    }

    private async Task RefreshStatesAsync()
    {
        var states = await Task.Run(() => Items.Select(i =>
        {
            try
            {
                return i.Tweak.IsApplied();
            }
            catch
            {
                return false;
            }
        }).ToList());
        for (int i = 0; i < Items.Count; i++) Items[i].IsApplied = states[i];
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedProfile = Profiles[3];
        foreach (var i in Items) i.IsSelected = false;
    }

    private List<Tweak>? PickSelected()
    {
        var picked = Items.Where(i => i.IsSelected).Select(i => i.Tweak).ToList();
        if (picked.Count == 0)
        {
            Say(Loc.T("Opt_NothingSelected"), 2);
            return null;
        }
        return picked;
    }

    private void Say(string text, int kind)
    {
        Status = text;
        StatusKind = kind;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (PickSelected() is not { } picked) return;
        var details = string.Join("\n\n", picked.Select(t => $"• {t.Name}\n   {t.Preview()}"));
        if (!ConfirmDialog.Confirm(Loc.T("Opt_Apply"), Loc.T("Opt_ConfirmApply"), details, Loc.T("Opt_ApplyShort"))) return;

        IsBusy = true;
        string prefix = "";
        switch (await Task.Run(TweakExecutor.EnsureRestorePoint))
        {
            case TweakExecutor.RestoreResult.Created:
                prefix = Loc.T("Opt_RestoreOk") + "\n";
                break;
            case TweakExecutor.RestoreResult.RecentExists:
                prefix = Loc.T("Opt_RestoreRecent") + "\n";
                break;
            default:
                if (!ConfirmDialog.Confirm(Loc.T("Opt_ConfirmTitle"), Loc.T("Opt_RestoreFail"), null, Loc.T("Opt_Continue")))
                {
                    IsBusy = false;
                    return;
                }
                break;
        }

        var results = await Task.Run(() => TweakExecutor.Apply(picked));
        ShowResults(results, prefix);
        await RefreshStatesAsync();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task RevertAsync()
    {
        if (PickSelected() is not { } picked) return;
        var names = string.Join("\n", picked.Select(t => $"• {t.Name}"));
        if (!ConfirmDialog.Confirm(Loc.T("Opt_Revert"), Loc.T("Opt_ConfirmRevert"), names, Loc.T("Opt_RevertShort"))) return;

        IsBusy = true;
        var results = await Task.Run(() => TweakExecutor.Revert(picked));
        ShowResults(results, "");
        await RefreshStatesAsync();
        IsBusy = false;
    }

    [RelayCommand]
    private static void OpenCleanup() =>
        (System.Windows.Application.Current.MainWindow?.DataContext as MainViewModel)?.OpenCommand.Execute("Cleanup");

    [RelayCommand]
    private async Task CleanupAsync()
    {
        if (!ConfirmDialog.Confirm(Loc.T("Opt_Cleanup"), Loc.T("Opt_ConfirmCleanup"), null, Loc.T("Opt_CleanupShort"))) return;
        IsBusy = true;
        var freed = await Task.Run(TweakExecutor.CleanTemp);
        Say($"{freed / 1024d / 1024d:0.#} MB {Loc.T("Opt_Freed")}", 1);
        IsBusy = false;
    }

    private void ShowResults(List<TweakResult> results, string prefix)
    {
        var lines = results.Select(r => $"{r.Tweak.Name}: " + r.Outcome switch
        {
            TweakOutcome.Applied => Loc.T("Opt_Applied"),
            TweakOutcome.Reverted => Loc.T("Opt_Reverted"),
            TweakOutcome.SkippedNeedsAdmin => Loc.T("Opt_Skipped"),
            TweakOutcome.NothingToUndo => Loc.T("Opt_NoBackup"),
            _ => $"{Loc.T("Opt_Error")} ({r.Detail})",
        }).ToList();
        if (results.Any(r => r.Outcome == TweakOutcome.Applied && r.Tweak.NeedsReboot))
            lines.Add(Loc.T("Opt_RebootNote"));
        bool ok = results.All(r => r.Outcome is TweakOutcome.Applied or TweakOutcome.Reverted);
        Say(prefix + string.Join("\n", lines), ok ? 1 : 2);
    }

    public override void Dispose() => Loc.Instance.PropertyChanged -= OnLocChanged;
}


