using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Otimizador.App.ViewModels;

public partial class NavItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Glyph { get; init; }
    public required LocText Label { get; init; }

    [ObservableProperty]
    private bool _isActive;
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(PageSubtitle))]
    private PageViewModel? _currentPage;

    [ObservableProperty] private string _cpuChip = "";
    [ObservableProperty] private string _gpuChip = "";
    [ObservableProperty] private string _ramChip = "";
    [ObservableProperty] private string _themeGlyph = "";

    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem { Key = "Dashboard", Glyph = "", Label = new LocText("Nav_Dashboard") },
        new NavItem { Key = "Hardware", Glyph = "", Label = new LocText("Nav_Hardware") },
        new NavItem { Key = "Monitor", Glyph = "", Label = new LocText("Nav_Monitor") },
        new NavItem { Key = "Space", Glyph = "\uE8B7", Label = new LocText("Nav_Space") },
        new NavItem { Key = "Cleanup", Glyph = "\uE74D", Label = new LocText("Nav_Cleanup") },
        new NavItem { Key = "Startup", Glyph = "\uE768", Label = new LocText("Nav_Startup") },
        new NavItem { Key = "Security", Glyph = "\uE83D", Label = new LocText("Nav_Security") },
        new NavItem { Key = "Test", Glyph = "", Label = new LocText("Nav_Test") },
        new NavItem { Key = "Optimize", Glyph = "", Label = new LocText("Nav_Optimize") },
        new NavItem { Key = "History", Glyph = "\uE81C", Label = new LocText("Nav_History") },
        new NavItem { Key = "Inventory", Glyph = "\uE8F1", Label = new LocText("Nav_Inventory") },
        new NavItem { Key = "Settings", Glyph = "\uE9E9", Label = new LocText("Nav_Settings") },
        new NavItem { Key = "About", Glyph = "\uE946", Label = new LocText("Nav_About") },
    };

    public string PageTitle => CurrentPage is null ? "" : Loc.T(CurrentPage.TitleKey);

    public string PageSubtitle => CurrentPage is null ? "" : Loc.T(CurrentPage.SubtitleKey);

    public string LanguageLabel => Loc.T("Language");

    public bool IsPortuguese => Loc.Instance.Lang == "pt";

    public bool IsEnglish => Loc.Instance.Lang == "en";

    public string AdminText => Loc.T(AppServices.IsAdmin ? "Admin_Yes" : "Admin_No");

    public bool IsAdmin => AppServices.IsAdmin;

    [ObservableProperty] private string _updateText = "";

    public bool HasUpdate => !string.IsNullOrEmpty(UpdateText);

    public bool NotAdmin => !AppServices.IsAdmin;

    public string VersionText => "v" + AppInfo.VersionText;

    public MainViewModel()
    {
        Loc.Instance.PropertyChanged += OnLocChanged;
        ThemeService.Changed += UpdateThemeGlyph;
        TelemetryService.Instance.Updated += OnTelemetry;
        Updates.UpdateWatcher.Changed += OnUpdateChanged;
        UpdateThemeGlyph();
        Open("Dashboard");
    }

    private void UpdateThemeGlyph() =>
        // No tema escuro o botão oferece o claro (sol) e vice-versa (lua).
        ThemeGlyph = ThemeService.Current == AppTheme.Dark ? "" : "";

    partial void OnUpdateTextChanged(string value) => OnPropertyChanged(nameof(HasUpdate));

    private void OnUpdateChanged() =>
        UpdateText = Updates.UpdateWatcher.Available is { } u ? $"{Loc.T("Atualização", "Update")} {u.Version.Major}.{u.Version.Minor}.{u.Version.Build}" : "";

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnUpdateChanged();
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(IsPortuguese));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(AdminText));
    }

    private void OnTelemetry(TelemetrySample s)
    {
        CpuChip = s.CpuTemp is { } ct ? $"CPU {ct:0} °C" : s.CpuLoad is { } cl ? $"CPU {cl:0}%" : "";
        GpuChip = s.GpuTemp is { } gt ? $"GPU {gt:0} °C" : s.GpuLoad is { } gl ? $"GPU {gl:0}%" : "";
        RamChip = s.RamLoad is { } r ? $"RAM {r:0}%" : "";
    }

    /// <summary>Abre uma página. Aceita "Optimize:safe" para já marcar um perfil.</summary>
    [RelayCommand]
    private void Open(string target)
    {
        var parts = target.Split(':', 2);
        var key = parts[0];
        var arg = parts.Length > 1 ? parts[1] : null;

        CurrentPage?.Dispose();
        CurrentPage = key switch
        {
            "Hardware" => new HardwareViewModel(),
            "Monitor" => new MonitorViewModel(),
            "Space" => new SpaceViewModel(),
            "Cleanup" => new CleanupViewModel(),
            "Startup" => new StartupViewModel(),
            "History" => new HistoryViewModel(),
            "Security" => new SecurityViewModel(),
            "Inventory" => new InventoryViewModel(),
            "Settings" => new SettingsViewModel(),
            "About" => new AboutViewModel(),
            "Test" => new TestViewModel(),
            "Optimize" => new OptimizeViewModel(arg),
            _ => new DashboardViewModel(Open),
        };
        var active = NavItems.Any(n => n.Key == key) ? key : "Dashboard";
        foreach (var n in NavItems) n.IsActive = n.Key == active;
    }

    [RelayCommand]
    private void GoSettings() => Open("Settings");

    [RelayCommand]
    private void ToggleTheme() => ThemeService.Toggle();

    [RelayCommand]
    private void SetLanguage(string lang)
    {
        Loc.Instance.SetLanguage(lang);
        ThemeService.SaveLanguage();
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        Loc.Instance.Toggle();
        ThemeService.SaveLanguage();
    }
}







