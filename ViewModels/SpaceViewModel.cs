using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Dialogs;
using Otimizador.App.Disk;

namespace Otimizador.App.ViewModels;

public sealed record DriveOption(string Root, string Display, string Detail, double UsedPercent, long UsedBytes);

public sealed record ListModeOption(LocText Title, string Key);

public sealed class DuplicateGroupItem
{
    public required DuplicateGroup Group { get; init; }
    public required ObservableCollection<SpaceEntry> Files { get; init; }
    public string Header => $"{Group.Files.Count} {Loc.T("Space_Copies")} · {SizeFormat.Bytes(Group.Size)} {Loc.T("Space_Each")} · {SizeFormat.Bytes(Group.Wasted)} {Loc.T("Space_Wasted")}";
}

public partial class CategoryStat : ObservableObject
{
    public required LocText Title { get; init; }
    public required Brush Brush { get; init; }
    public required string SizeText { get; init; }
    public required double Percent { get; init; }
}

/// <summary>Linha das listas "Arquivos grandes" e "Pastas" (a mesma classe serve para os dois).</summary>
public partial class SpaceEntry : ObservableObject
{
    public DiskNode Node { get; }
    public double Percent { get; }

    [ObservableProperty] private bool _isSelected;

    public string Name => Node.Name;
    public string Folder => Path.GetDirectoryName(Node.FullPath) ?? Node.FullPath;
    public string SizeText => SizeFormat.Bytes(Node.Size);
    public string DateText => Node.Modified == default ? "" : Node.Modified.ToString("d");
    public bool IsDir => Node.IsDir;
    public Brush Brush => Node.IsDir ? FolderBrush : FileCategories.BrushOf(Node.Category);
    public bool IsProtected { get; }
    public bool CanSelect => !IsProtected;

    private static readonly Brush FolderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)));

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public SpaceEntry(DiskNode node, double percent)
    {
        Node = node;
        Percent = percent;
        IsProtected = SafeDelete.IsProtected(node.FullPath);
    }
}

public partial class SpaceViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;
    private ScanResult? _result;
    private long _driveUsedBytes;

    public override string TitleKey => "Nav_Space";

    public override string SubtitleKey => "Sub_Space";

    public DriveOption[] Drives { get; }

    public ListModeOption[] ListModes { get; } =
    {
        new(new LocText("Space_BigFiles"), "files"),
        new(new LocText("Space_Folders"), "folders"),
        new(new LocText("Space_Duplicates"), "dups"),
    };

    public ObservableCollection<SpaceEntry> TopFiles { get; } = new();
    public ObservableCollection<SpaceEntry> Folders { get; } = new();
    public ObservableCollection<CategoryStat> Categories { get; } = new();

    [ObservableProperty] private DriveOption? _selectedDrive;
    [ObservableProperty] private ListModeOption _selectedMode;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isScanning;

    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private string _currentPath = "";

    [ObservableProperty] private DiskNode? _viewRoot;
    [ObservableProperty] private int _treemapVersion;
    [ObservableProperty] private string _hoverText = "";
    [ObservableProperty] private bool _canGoUp;

    [ObservableProperty] private string _driveTotalText = "";
    [ObservableProperty] private string _driveUsedText = "";
    [ObservableProperty] private string _driveFreeText = "";
    [ObservableProperty] private bool _partialScan;
    [ObservableProperty] private bool _showProtected;
    [ObservableProperty] private string _hiddenProtectedText = "";

    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _selectedSizeText = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _statusKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsScanning && !IsBusy;

    public bool ShowFiles => SelectedMode.Key == "files";

    public bool ShowFolders => SelectedMode.Key == "folders";

    public bool ShowDuplicates => SelectedMode.Key == "dups";

    public ObservableCollection<DuplicateGroupItem> Duplicates { get; } = new();

    private CancellationTokenSource? _dupCts;

    [ObservableProperty] private bool _isFindingDuplicates;
    [ObservableProperty] private double _duplicateProgress;
    [ObservableProperty] private string _duplicateStatus = "";

    private IEnumerable<SpaceEntry> AllEntries() => TopFiles.Concat(Folders).Concat(Duplicates.SelectMany(g => g.Files));

    public SpaceViewModel()
    {
        Drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d =>
            {
                long used = d.TotalSize - d.AvailableFreeSpace;
                return new DriveOption(d.Name, d.Name.TrimEnd('\\'),
                    $"{SizeFormat.Bytes(used)} / {SizeFormat.Bytes(d.TotalSize)}", used * 100.0 / d.TotalSize, used);
            })
            .ToArray();
        _selectedMode = ListModes[0];
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        _selectedDrive = Drives.FirstOrDefault(d => d.Root.Equals(systemRoot, StringComparison.OrdinalIgnoreCase)) ?? Drives.FirstOrDefault();
    }

    partial void OnSelectedModeChanged(ListModeOption value)
    {
        OnPropertyChanged(nameof(ShowFiles));
        OnPropertyChanged(nameof(ShowFolders));
        OnPropertyChanged(nameof(ShowDuplicates));
    }

    private bool CanScan() => !IsScanning && !IsBusy && SelectedDrive is not null;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (SelectedDrive is not { } drive) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsScanning = true;
        HasResult = false;
        Status = "";
        Progress = 0;
        ScanText = Loc.T("Space_Scanning");
        _driveUsedBytes = drive.UsedBytes;

        var progress = new Progress<ScanProgress>(p =>
        {
            ScanText = $"{p.Files:N0} {Loc.T("Space_Files")}  ·  {SizeFormat.Bytes(p.Bytes)}";
            CurrentPath = p.CurrentPath;
            Progress = _driveUsedBytes > 0 ? Math.Min(99, p.Bytes * 100.0 / _driveUsedBytes) : 0;
        });

        try
        {
            var result = await Task.Run(() => new DiskScanner().Scan(drive.Root, progress, ct), ct);
            Apply(result, drive);
        }
        catch (OperationCanceledException)
        {
            Say(Loc.T("Space_Cancelled"), 2);
        }
        catch (Exception ex)
        {
            Say($"{Loc.T("Opt_Error")}: {ex.Message}", 2);
        }
        finally
        {
            IsScanning = false;
            CurrentPath = "";
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void Apply(ScanResult result, DriveOption drive)
    {
        _result = result;
        Progress = 100;

        Categories.Clear();
        long total = Math.Max(1, result.CategorySizes.Sum());
        foreach (var cat in Enum.GetValues<FileCategory>().OrderByDescending(c => result.CategorySizes[(int)c]))
        {
            long size = result.CategorySizes[(int)cat];
            if (size <= 0) continue;
            Categories.Add(new CategoryStat
            {
                Title = new LocText(FileCategories.LocKey(cat)), Brush = FileCategories.BrushOf(cat),
                SizeText = SizeFormat.Bytes(size), Percent = size * 100.0 / total,
            });
        }

        RebuildFileList();

        PartialScan = result.Bytes < _driveUsedBytes * 0.85;
        SetViewRoot(result.Root);
        RefreshDriveInfo();
        HasResult = true;
        UpdateSelection();
    }

    partial void OnShowProtectedChanged(bool value)
    {
        RebuildFileList();
        if (ViewRoot is { } root) SetViewRoot(root);
    }

    /// <summary>Monta a lista de arquivos grandes; por padrão sem os protegidos (Windows e programas instalados).</summary>
    private void RebuildFileList()
    {
        foreach (var e in TopFiles) e.PropertyChanged -= OnEntryChanged;
        TopFiles.Clear();
        if (_result is null) return;
        int hidden = 0;
        foreach (var n in _result.TopFiles)
        {
            bool prot = SafeDelete.IsProtected(n.FullPath);
            if (prot && !ShowProtected)
            {
                hidden++;
                continue;
            }
            if (TopFiles.Count < 120) AddFileEntry(TopFiles.Count, n);
        }
        HiddenProtectedText = hidden > 0 && !ShowProtected ? $"{hidden} {Loc.T("Space_HiddenProtected")}" : "";
        UpdateSelection();
    }

    [RelayCommand]
    private static void OpenInstalledApps()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
        }
        catch
        {
            // sem o app Configurações
        }
    }

    private void AddFileEntry(int index, DiskNode node)
    {
        var entry = new SpaceEntry(node, _result is { Root.Size: > 0 } r ? node.Size * 100.0 / r.Root.Size : 0);
        entry.PropertyChanged += OnEntryChanged;
        TopFiles.Insert(Math.Min(index, TopFiles.Count), entry);
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpaceEntry.IsSelected)) UpdateSelection();
    }

    private void SetViewRoot(DiskNode node)
    {
        ViewRoot = node;
        CanGoUp = node.Parent is not null;

        foreach (var f in Folders) f.PropertyChanged -= OnEntryChanged;
        Folders.Clear();
        double rootSize = Math.Max(1, node.Size);
        foreach (var c in node.Children.Where(c => c.IsDir && !c.IsOther && (ShowProtected || !SafeDelete.IsProtected(c.FullPath))).Take(80))
        {
            var entry = new SpaceEntry(c, c.Size * 100.0 / rootSize);
            entry.PropertyChanged += OnEntryChanged;
            Folders.Add(entry);
        }
        TreemapVersion++;
        UpdateSelection();
    }

    private void RefreshDriveInfo()
    {
        try
        {
            var d = new DriveInfo(SelectedDrive!.Root);
            DriveTotalText = SizeFormat.Bytes(d.TotalSize);
            DriveUsedText = SizeFormat.Bytes(d.TotalSize - d.AvailableFreeSpace);
            DriveFreeText = SizeFormat.Bytes(d.AvailableFreeSpace);
        }
        catch
        {
            // disco removido
        }
    }

    private void UpdateSelection()
    {
        var picked = AllEntries().Where(e => e.IsSelected).DistinctBy(e => e.Node).ToList();
        SelectedCount = picked.Count;
        // evita contar duas vezes um arquivo dentro de uma pasta também marcada
        long total = picked.Where(e => !picked.Any(o => o != e && o.IsDir && e.Node.IsInside(o.Node.FullPath))).Sum(e => e.Node.Size);
        SelectedSizeText = SizeFormat.Bytes(total);
    }

    private void Say(string text, int kind)
    {
        Status = text;
        StatusKind = kind;
    }

    [RelayCommand]
    private void Zoom(DiskNode? node)
    {
        if (node is { IsDir: true, IsOther: false }) SetViewRoot(node);
    }

    [RelayCommand]
    private void Up()
    {
        if (ViewRoot?.Parent is { } p) SetViewRoot(p);
    }

    /// <summary>Clique num arquivo do mapa: destaca-o na lista (incluindo-o se ainda não estiver).</summary>
    [RelayCommand]
    private void SelectNode(DiskNode? node)
    {
        if (node is null) return;
        var entry = TopFiles.FirstOrDefault(e => ReferenceEquals(e.Node, node));
        if (entry is null)
        {
            AddFileEntry(0, node);
            entry = TopFiles[0];
        }
        SelectedMode = ListModes[0];
        if (entry.CanSelect) entry.IsSelected = !entry.IsSelected;
    }

    [RelayCommand]
    private static void OpenInExplorer(SpaceEntry? entry)
    {
        if (entry is null) return;
        try
        {
            var arg = entry.IsDir ? $"\"{entry.Node.FullPath}\"" : $"/select,\"{entry.Node.FullPath}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true });
        }
        catch
        {
            // pasta já não existe
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var e in AllEntries()) e.IsSelected = false;
    }

    [RelayCommand]
    private async Task TrashSelectedAsync()
    {
        var picked = AllEntries().Where(e => e.IsSelected && e.CanSelect).DistinctBy(e => e.Node).ToList();
        if (picked.Count == 0)
        {
            Say(Loc.T("Space_NothingSelected"), 2);
            return;
        }
        // Se uma pasta inteira está marcada, os arquivos dentro dela já vão junto.
        var top = picked.Where(e => !picked.Any(o => o != e && o.IsDir && e.Node.IsInside(o.Node.FullPath))).ToList();
        long total = top.Sum(e => e.Node.Size);

        var details = string.Join("\n\n", top.Select(e => $"• {e.Name}  ({e.SizeText})\n   {e.Node.FullPath}"));
        var warning = top.Any(e => e.IsDir) ? "\n\n" + Loc.T("Space_FolderWarning") : "";
        if (!ConfirmDialog.Confirm(Loc.T("Space_TrashTitle"),
                $"{Loc.T("Space_ConfirmTrash")} {SizeFormat.Bytes(total)}.{warning}", details, Loc.T("Space_Trash"))) return;

        IsBusy = true;
        var outcome = await Task.Run(() => top.Select(e => (Entry: e, Error: SafeDelete.ToRecycleBin(e.Node.FullPath, e.IsDir))).ToList());

        long freed = 0;
        int ok = 0, failed = 0;
        foreach (var (entry, error) in outcome)
        {
            if (error is not null)
            {
                failed++;
                continue;
            }
            ok++;
            freed += entry.Node.Size;
            RemoveNode(entry.Node);
        }

        RefreshDriveInfo();
        TreemapVersion++;
        UpdateSelection();
        Say($"{ok} {Loc.T("Space_Moved")} · {SizeFormat.Bytes(freed)} {Loc.T("Space_Freed")}"
            + (failed > 0 ? $"\n{failed} {Loc.T("Space_Failed")}" : "") + "\n" + Loc.T("Space_RescanHint"),
            failed > 0 ? 2 : 1);
        IsBusy = false;
    }

    private void RemoveNode(DiskNode node)
    {
        string path = node.FullPath;
        bool viewInside = ViewRoot is not null && ViewRoot.IsInside(path);
        var parent = node.Parent;
        node.Detach();

        foreach (var list in new[] { TopFiles, Folders })
            foreach (var e in list.Where(e => e.Node.IsInside(path)).ToList())
            {
                e.PropertyChanged -= OnEntryChanged;
                list.Remove(e);
            }
        foreach (var g in Duplicates.ToList())
        {
            foreach (var gone in g.Files.Where(e => e.Node.IsInside(path)).ToList()) g.Files.Remove(gone);
            if (g.Files.Count < 2) Duplicates.Remove(g);
        }
        if (viewInside && parent is not null) SetViewRoot(parent);
    }

    [RelayCommand]
    private async Task FindDuplicatesAsync()
    {
        if (_result is null || IsFindingDuplicates) return;
        _dupCts = new CancellationTokenSource();
        var ct = _dupCts.Token;
        IsFindingDuplicates = true;
        DuplicateProgress = 0;
        DuplicateStatus = Loc.T("Space_DupSearching");
        foreach (var g in Duplicates) foreach (var e in g.Files) e.PropertyChanged -= OnEntryChanged;
        Duplicates.Clear();

        var progress = new Progress<(int Done, int Total)>(p =>
            DuplicateProgress = p.Total == 0 ? 100 : p.Done * 100.0 / p.Total);
        try
        {
            var groups = await Task.Run(() => DuplicateFinder.Find(_result.Root, progress, ct), ct);
            foreach (var g in groups)
            {
                var files = new ObservableCollection<SpaceEntry>(g.Files.Select(n =>
                {
                    var entry = new SpaceEntry(n, 0);
                    entry.PropertyChanged += OnEntryChanged;
                    return entry;
                }));
                Duplicates.Add(new DuplicateGroupItem { Group = g, Files = files });
            }
            long wasted = groups.Sum(g => g.Wasted);
            DuplicateStatus = groups.Count == 0
                ? Loc.T("Space_DupNone")
                : $"{groups.Count} {Loc.T("Space_DupGroups")} · {SizeFormat.Bytes(wasted)} {Loc.T("Space_Wasted")}";
        }
        catch (OperationCanceledException)
        {
            DuplicateStatus = Loc.T("Space_Cancelled");
        }
        finally
        {
            IsFindingDuplicates = false;
        }
    }

    [RelayCommand]
    private void CancelDuplicates() => _dupCts?.Cancel();

    /// <summary>Marca todas as cópias menos a mais antiga de cada grupo (que fica como original).</summary>
    [RelayCommand]
    private void MarkDuplicates()
    {
        foreach (var g in Duplicates)
            for (int i = 0; i < g.Files.Count; i++)
                g.Files[i].IsSelected = i > 0 && g.Files[i].CanSelect;
    }

    public override void Dispose()
    {
        _cts?.Cancel();
        _dupCts?.Cancel();
    }
}




