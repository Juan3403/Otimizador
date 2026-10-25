using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Cleanup;
using Otimizador.App.Dialogs;
using Otimizador.App.Disk;

namespace Otimizador.App.ViewModels;

public partial class CleanupItem : ObservableObject
{
    public CleanupCategory Category { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private long _size;
    [ObservableProperty] private string _sizeText = "…";
    [ObservableProperty] private bool _measured;

    public CleanupItem(CleanupCategory category)
    {
        Category = category;
        _isSelected = category.DefaultSelected;
    }

    public string Name => Category.Name;
    public string Description => Category.Description;
    public bool NeedsAdmin => Category.NeedsAdmin;

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
}

public partial class AppRemovalItem : ObservableObject
{
    public PreinstalledApp App { get; }

    [ObservableProperty] private bool _isSelected;

    public AppRemovalItem(PreinstalledApp app) => App = app;

    public string Name => App.Name;
}

public partial class CleanupViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public override string TitleKey => "Nav_Cleanup";

    public override string SubtitleKey => "Sub_Cleanup";

    public ObservableCollection<CleanupItem> Items { get; } = new();
    public ObservableCollection<AppRemovalItem> Apps { get; } = new();

    [ObservableProperty] private string _totalText = "0 B";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _statusKind;
    [ObservableProperty] private string _appsStatus = "";
    [ObservableProperty] private int _selectedApps;
    [ObservableProperty] private bool _appsLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty] private bool _measuring = true;

    public bool IsIdle => !IsBusy;

    public CleanupViewModel()
    {
        foreach (var c in CleanupService.All)
        {
            var item = new CleanupItem(c);
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);
        }
        Loc.Instance.PropertyChanged += OnLocChanged;
        _ = MeasureAsync();
        _ = LoadAppsAsync();
    }

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var i in Items) i.RefreshLanguage();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupItem.IsSelected) or nameof(CleanupItem.Size)) UpdateTotal();
    }

    private void UpdateTotal() => TotalText = SizeFormat.Bytes(Items.Where(i => i.IsSelected).Sum(i => i.Size));

    [RelayCommand]
    private async Task MeasureAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Measuring = true;
        foreach (var i in Items)
        {
            i.Measured = false;
            i.SizeText = "…";
        }

        var gate = new SemaphoreSlim(4);
        var tasks = Items.Select(async item =>
        {
            await gate.WaitAsync(ct);
            try
            {
                long size = await Task.Run(() =>
                {
                    try
                    {
                        return CleanupService.Measure(item.Category, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return -1L;
                    }
                }, ct);
                if (size >= 0)
                {
                    item.Size = size;
                    item.SizeText = SizeFormat.Bytes(size);
                    item.Measured = true;
                }
            }
            catch (OperationCanceledException)
            {
                // nova análise iniciada
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Measuring = false;
        UpdateTotal();
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        var picked = Items.Where(i => i.IsSelected).ToList();
        if (picked.Count == 0)
        {
            Say(Loc.T("Clean_NothingSelected"), 2);
            return;
        }
        var list = string.Join("\n", picked.Select(i => $"• {i.Name}  ({i.SizeText})"));
        var warning = picked.Any(i => i.Category.IsRecycleBin) ? "\n\n" + Loc.T("Clean_RecycleWarning") : "";
        if (!ConfirmDialog.Confirm(Loc.T("Clean_Button"), $"{Loc.T("Clean_Confirm")} {TotalText}.{warning}", list, Loc.T("Clean_Do"))) return;

        IsBusy = true;
        long freed = await Task.Run(() => picked.Sum(i => CleanupService.Clean(i.Category)));
        EventLog.Add("cleanup", $"{SizeFormat.Bytes(freed)} liberados ({picked.Count} categorias)");
        Say($"{SizeFormat.Bytes(freed)} {Loc.T("Opt_Freed")}", 1);
        IsBusy = false;
        await MeasureAsync();
    }

    private void Say(string text, int kind)
    {
        Status = text;
        StatusKind = kind;
    }

    // ---- Aplicativos pré-instalados ----
    private async Task LoadAppsAsync()
    {
        AppsLoading = true;
        var installed = await Task.Run(PreinstalledApps.Installed);
        Apps.Clear();
        foreach (var a in installed)
        {
            var item = new AppRemovalItem(a);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppRemovalItem.IsSelected)) SelectedApps = Apps.Count(x => x.IsSelected);
            };
            Apps.Add(item);
        }
        AppsStatus = installed.Count == 0 ? Loc.T("Apps_None") : "";
        AppsLoading = false;
    }

    [RelayCommand]
    private async Task RemoveAppsAsync()
    {
        var picked = Apps.Where(a => a.IsSelected).ToList();
        if (picked.Count == 0)
        {
            AppsStatus = Loc.T("Apps_NothingSelected");
            return;
        }
        var list = string.Join("\n", picked.Select(a => $"• {a.Name}"));
        if (!ConfirmDialog.Confirm(Loc.T("Apps_Remove"), Loc.T("Apps_Confirm"), list, Loc.T("Apps_RemoveShort"))) return;

        IsBusy = true;
        AppsStatus = Loc.T("Apps_Removing");
        var results = await Task.Run(() => picked.Select(a => (App: a, Error: PreinstalledApps.Remove(a.App))).ToList());
        int ok = results.Count(r => r.Error is null);
        EventLog.Add("apps", $"{ok} aplicativos removidos");
        foreach (var r in results.Where(r => r.Error is null)) Apps.Remove(r.App);
        SelectedApps = Apps.Count(x => x.IsSelected);
        AppsStatus = $"{ok} {Loc.T("Apps_Removed")}" + (ok < results.Count ? $" · {results.Count - ok} {Loc.T("Space_Failed")}" : "");
        IsBusy = false;
    }

    public override void Dispose()
    {
        _cts?.Cancel();
        Loc.Instance.PropertyChanged -= OnLocChanged;
    }
}
