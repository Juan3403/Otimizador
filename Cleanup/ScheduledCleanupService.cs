using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Otimizador.App.Alerts;
using Otimizador.App.Disk;

namespace Otimizador.App.Cleanup;

/// <summary>
/// Limpeza semanal automática (desligada por padrão). Roda enquanto o app está aberto (ele pode ficar na bandeja e abrir com o
/// Windows). Limpa só as categorias que vêm marcadas por padrão na aba Limpeza: nunca a Lixeira nem os navegadores.
/// </summary>
public sealed partial class ScheduledCleanupService : ObservableObject
{
    public static ScheduledCleanupService Instance { get; } = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool _loading = true;
    private bool _running;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _weekday;
    [ObservableProperty] private int _hour;
    [ObservableProperty] private string _lastRunText = "";

    public static readonly DayOfWeek[] Days =
    {
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
    };

    private ScheduledCleanupService()
    {
        var s = Settings.Load();
        _enabled = s.ScheduledCleanup;
        _weekday = s.CleanupWeekday;
        _hour = s.CleanupHour;
        _loading = false;
        UpdateLastText(s.LastScheduledCleanup);
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_loading) return;
        var s = Settings.Load();
        s.ScheduledCleanup = value;
        if (value) s.LastScheduledCleanup = DateTime.Now; // a primeira limpeza é na próxima data marcada, não agora
        s.Save();
        UpdateLastText(s.LastScheduledCleanup);
    }

    [RelayCommand]
    private void AdjustHour(string delta) => Hour = Math.Clamp(Hour + int.Parse(delta), 0, 23);

    partial void OnWeekdayChanged(int value) => SaveSchedule();
    partial void OnHourChanged(int value) => SaveSchedule();

    private void SaveSchedule()
    {
        if (_loading) return;
        var s = Settings.Load();
        s.CleanupWeekday = Weekday;
        s.CleanupHour = Hour;
        s.Save();
    }

    private void UpdateLastText(DateTime? last) =>
        LastRunText = last is null ? "" : $"{Loc.T("Sched_Last")}: {last:g}";

    /// <summary>Próximo horário marcado depois de <paramref name="last"/> (o dia da semana e a hora escolhidos).</summary>
    public static DateTime NextDue(DateTime last, int weekday, int hour)
    {
        for (int i = 0; i < 8; i++)
        {
            var candidate = last.Date.AddDays(i).AddHours(hour);
            if ((int)candidate.DayOfWeek == weekday && candidate > last) return candidate;
        }
        return last.AddDays(7);
    }

    private async Task TickAsync()
    {
        if (!Enabled || _running) return;
        var s = Settings.Load();
        var last = s.LastScheduledCleanup ?? DateTime.Now;
        if (DateTime.Now < NextDue(last, Weekday, Hour)) return;

        _running = true;
        try
        {
            long freed = await Task.Run(() => CleanupService.All
                .Where(c => c.DefaultSelected && !c.IsRecycleBin && (!c.NeedsAdmin || AppServices.IsAdmin))
                .Sum(c => CleanupService.Clean(c)));
            var now = DateTime.Now;
            s = Settings.Load();
            s.LastScheduledCleanup = now;
            s.Save();
            UpdateLastText(now);
            EventLog.Add("cleanup", $"Limpeza agendada: {SizeFormat.Bytes(freed)} liberados");
            ToastWindow.Show(Loc.T("Limpeza agendada", "Scheduled cleanup"), $"{SizeFormat.Bytes(freed)} {Loc.T("Opt_Freed")}");
        }
        finally
        {
            _running = false;
        }
    }
}

