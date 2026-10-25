using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Otimizador.App.Cleanup;
using Otimizador.App.GameMode;
using Otimizador.App.Inventory;
using Otimizador.App.Tray;
using Otimizador.App.Updates;

namespace Otimizador.App.ViewModels;

public sealed record WeekdayOption(LocText Title, int Day);

public partial class SettingsViewModel : PageViewModel
{
    private bool _loading = true;

    public override string TitleKey => "Nav_Settings";

    public override string SubtitleKey => "Sub_Settings";

    public GameModeService GameMode => GameModeService.Instance;

    public ScheduledCleanupService Scheduled => ScheduledCleanupService.Instance;

    public WeekdayOption[] Weekdays { get; } = ScheduledCleanupService.Days
        .Select(d => new WeekdayOption(new LocText("Day_" + d), (int)d)).ToArray();

    public WeekdayOption SelectedWeekday
    {
        get => Weekdays.First(w => w.Day == Scheduled.Weekday);
        set
        {
            if (value is null) return;
            Scheduled.Weekday = value.Day;
            OnPropertyChanged();
        }
    }

    public string VersionText => $"{AppInfo.Name} {AppInfo.VersionText}";

    public string InstallText => UpdateService.CurrentInstall() switch
    {
        UpdateService.InstallKind.Installed => Loc.T("Instalado (atualiza sozinho)", "Installed (updates itself)"),
        UpdateService.InstallKind.Portable => Loc.T("Portátil (atualiza trocando os arquivos)", "Portable (updates by replacing files)"),
        _ => Loc.T("Executando da pasta de desenvolvimento (atualização manual)", "Running from the development folder (manual update)"),
    };

    // Atualização
    [ObservableProperty] private string _repo = "";
    [ObservableProperty] private bool _autoCheck;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private int _updateKind;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _repoHint = "";

    // Bandeja e inicialização
    [ObservableProperty] private bool _trayEnabled;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _runAtLogon;
    [ObservableProperty] private string _systemStatus = "";

    public SettingsViewModel()
    {
        var s = Settings.Load();
        _repo = AppInfo.Repo;
        _autoCheck = s.AutoCheckUpdates;
        _trayEnabled = s.TrayEnabled;
        _closeToTray = s.CloseToTray;
        _startMinimized = s.StartMinimized;
        _runAtLogon = LogonTask.Exists();
        _loading = false;
        RepoHint = AppInfo.Repo.Length == 0 ? Loc.T("Set_RepoEmpty") : "";
        ShowAvailable(UpdateWatcher.Available);
    }

    private void Save(Action<Settings> change)
    {
        if (_loading) return;
        var s = Settings.Load();
        change(s);
        s.Save();
    }

    partial void OnRepoChanged(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            Save(s => s.UpdateRepo = null);
            RepoHint = AppInfo.Repo.Length == 0 ? Loc.T("Set_RepoEmpty") : "";
        }
        else if (AppInfo.IsValidRepo(text))
        {
            Save(s => s.UpdateRepo = text);
            RepoHint = "";
        }
        else
        {
            RepoHint = Loc.T("Set_RepoInvalid");
        }
    }

    partial void OnAutoCheckChanged(bool value) => Save(s => s.AutoCheckUpdates = value);
    partial void OnCloseToTrayChanged(bool value) => Save(s => s.CloseToTray = value);
    partial void OnStartMinimizedChanged(bool value) => Save(s => s.StartMinimized = value);

    partial void OnTrayEnabledChanged(bool value)
    {
        Save(s => s.TrayEnabled = value);
        if (_loading) return;
        if (value) TrayService.Init(Application.Current.MainWindow!);
        else TrayService.Dispose();
    }

    partial void OnRunAtLogonChanged(bool value)
    {
        if (_loading) return;
        if (!LogonTask.Set(value))
        {
            SystemStatus = Loc.T("Set_LogonFailed");
            _loading = true;
            RunAtLogon = !value;
            _loading = false;
        }
        else
        {
            SystemStatus = value ? Loc.T("Set_LogonOn") : Loc.T("Set_LogonOff");
        }
    }

    private void ShowAvailable(UpdateInfo? info)
    {
        UpdateAvailable = info is not null;
        if (info is null) return;
        Notes = info.Notes.Length > 600 ? info.Notes[..600] + "…" : info.Notes;
        UpdateStatus = $"{Loc.T("Set_NewVersion")} {info.Version.Major}.{info.Version.Minor}.{info.Version.Build}";
        UpdateKind = 1;
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        IsChecking = true;
        UpdateStatus = Loc.T("Set_Checking");
        UpdateKind = 0;
        var result = await UpdateService.CheckAsync();
        UpdateWatcher.Set(result.State == UpdateState.Available ? result.Info : null);
        UpdateAvailable = false;
        switch (result.State)
        {
            case UpdateState.NotConfigured:
                UpdateStatus = Loc.T("Set_RepoEmpty");
                UpdateKind = 2;
                break;
            case UpdateState.NoRelease:
                UpdateStatus = Loc.T("Set_NoRelease");
                UpdateKind = 2;
                break;
            case UpdateState.Error:
                UpdateStatus = $"{Loc.T("Set_CheckFailed")} {result.Message}";
                UpdateKind = 2;
                break;
            case UpdateState.UpToDate:
                UpdateStatus = $"{Loc.T("Set_UpToDate")} ({AppInfo.VersionText})";
                UpdateKind = 1;
                break;
            case UpdateState.Available:
                ShowAvailable(result.Info);
                break;
        }
        IsChecking = false;
    }

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        var info = UpdateWatcher.Available;
        if (info is null) return;
        var kind = UpdateService.CurrentInstall();
        if (kind == UpdateService.InstallKind.Manual)
        {
            OpenUrl(info.PageUrl);
            return;
        }

        string? url = kind == UpdateService.InstallKind.Installed ? info.InstallerUrl : info.ZipUrl;
        if (url is null)
        {
            UpdateStatus = Loc.T("Set_NoAsset");
            UpdateKind = 2;
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        UpdateStatus = Loc.T("Set_Downloading");
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            var path = await UpdateService.DownloadAsync(info, url, progress, CancellationToken.None);
            UpdateStatus = Loc.T("Set_Installing");
            EventLog.Add("update", $"Atualizando para {info.Tag}");
            if (kind == UpdateService.InstallKind.Installed) UpdateService.RunInstaller(path);
            else UpdateService.RunPortableUpdate(path);
            GameModeService.Instance.Shutdown();
            TrayService.Exit();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"{Loc.T("Set_UpdateFailed")} {ex.Message}";
            UpdateKind = 2;
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenRepo() => OpenUrl(AppInfo.RepoUrl);

    [RelayCommand]
    private void OpenRelease()
    {
        if (UpdateWatcher.Available is { } info && info.PageUrl.Length > 0) OpenUrl(info.PageUrl);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (UpdateService.IsAllowedUrl(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // sem navegador padrão
        }
    }
}

