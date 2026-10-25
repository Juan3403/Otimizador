using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Security;

namespace Otimizador.App.ViewModels;

public sealed class SecurityRow
{
    public required SecurityItem Item { get; init; }
    public string Title => Item.Title;
    public string Detail => Item.Detail;
    public int Level => (int)Item.Level;
    public string LevelText => Item.Level switch
    {
        SecLevel.Good => Loc.T("OK", "OK"),
        SecLevel.Warning => Loc.T("Atenção", "Attention"),
        SecLevel.Bad => Loc.T("Problema", "Problem"),
        _ => Loc.T("Sem dados", "No data"),
    };
}

public partial class SecurityViewModel : PageViewModel
{
    public override string TitleKey => "Nav_Security";

    public override string SubtitleKey => "Sub_Security";

    public ObservableCollection<SecurityRow> Items { get; } = new();

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private double _scorePercent;
    [ObservableProperty] private string _scoreText = "—";
    [ObservableProperty] private string _status = "";

    public SecurityViewModel()
    {
        if (SecurityService.Last is { } last)
        {
            Fill(last);
        }
        else
        {
            _ = RunAsync();
        }
    }

    private void Fill(List<SecurityItem> items)
    {
        Items.Clear();
        foreach (var i in items) Items.Add(new SecurityRow { Item = i });
        var (good, total) = SecurityService.Score(items);
        ScorePercent = total == 0 ? 0 : good * 100.0 / total;
        ScoreText = total == 0 ? "—" : $"{good}/{total}";
        HasResult = Items.Count > 0;
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        Status = Loc.T("Sec_Checking");
        var items = await Task.Run(SecurityService.Check);
        Fill(items);

        // As atualizações do Windows demoram: mostramos o resto e completamos depois.
        Status = Loc.T("Sec_CheckingUpdates");
        var updates = await SecurityService.CheckUpdatesAsync();
        items.Add(updates);
        Fill(items);
        Status = $"{Loc.T("Sec_Done")} {DateTime.Now:t}";
        IsRunning = false;
    }
}

