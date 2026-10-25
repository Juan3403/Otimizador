using System.Globalization;

namespace Otimizador.App;

public sealed record HistoryPoint(DateTime Time, double CpuLoad, double CpuTemp, double GpuLoad, double GpuTemp, double Ram);

/// <summary>
/// Grava uma linha por minuto (média de uso, máxima de temperatura) em arquivos por dia, para ver o que aconteceu nas
/// últimas horas ou dias. Fica em Dados\history\AAAA-MM-DD.csv e só guarda números, nada pessoal.
/// </summary>
public sealed class HistoryService
{
    public static HistoryService Instance { get; } = new();

    private readonly List<float> _cl = new(), _ct = new(), _gl = new(), _gt = new(), _rm = new();
    private DateTime _minute;
    private bool _started;

    private static string Dir => Path.Combine(AppServices.DataDir, "history");

    public void Start()
    {
        if (_started) return;
        _started = true;
        TelemetryService.Instance.Updated += OnSample;
    }

    private void OnSample(TelemetrySample s)
    {
        var now = DateTime.Now;
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
        if (_minute == default) _minute = minute;
        if (minute != _minute)
        {
            Flush();
            _minute = minute;
        }
        if (s.CpuLoad is { } a) _cl.Add(a);
        if (s.CpuTemp is { } b) _ct.Add(b);
        if (s.GpuLoad is { } c) _gl.Add(c);
        if (s.GpuTemp is { } d) _gt.Add(d);
        if (s.RamLoad is { } e) _rm.Add(e);
    }

    private static string Fmt(List<float> v, bool max) =>
        v.Count == 0 ? "" : (max ? v.Max() : v.Average()).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>Grava o minuto em andamento (chamado ao trocar de minuto e ao fechar o app).</summary>
    public void Flush()
    {
        try
        {
            if (_minute == default || (_cl.Count + _ct.Count + _gl.Count + _gt.Count + _rm.Count) == 0) return;
            Directory.CreateDirectory(Dir);
            var line = string.Join(";", _minute.ToString("s", CultureInfo.InvariantCulture),
                Fmt(_cl, false), Fmt(_ct, true), Fmt(_gl, false), Fmt(_gt, true), Fmt(_rm, false));
            File.AppendAllText(Path.Combine(Dir, $"{_minute:yyyy-MM-dd}.csv"), line + Environment.NewLine);
        }
        catch
        {
            // histórico é opcional
        }
        finally
        {
            _cl.Clear(); _ct.Clear(); _gl.Clear(); _gt.Clear(); _rm.Clear();
        }
    }

    private static double P(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;

    public List<HistoryPoint> Read(TimeSpan range)
    {
        var points = new List<HistoryPoint>();
        var since = DateTime.Now - range;
        try
        {
            for (var day = since.Date; day <= DateTime.Today; day = day.AddDays(1))
            {
                var file = Path.Combine(Dir, $"{day:yyyy-MM-dd}.csv");
                if (!File.Exists(file)) continue;
                foreach (var line in File.ReadLines(file))
                {
                    var p = line.Split(';');
                    if (p.Length < 6 || !DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) || t < since) continue;
                    points.Add(new HistoryPoint(t, P(p[1]), P(p[2]), P(p[3]), P(p[4]), P(p[5])));
                }
            }
        }
        catch
        {
            // arquivo em uso
        }
        return points;
    }

    /// <summary>Reduz a série a N pontos (média, ou máxima para temperaturas); intervalos sem dados viram NaN.</summary>
    public static double[] Bucket(List<HistoryPoint> points, TimeSpan range, int buckets, Func<HistoryPoint, double> pick, bool max)
    {
        var result = new double[buckets];
        var sums = new double[buckets];
        var counts = new int[buckets];
        var maxes = new double[buckets];
        Array.Fill(maxes, double.NaN);
        var start = DateTime.Now - range;
        foreach (var p in points)
        {
            double v = pick(p);
            if (double.IsNaN(v)) continue;
            int i = (int)((p.Time - start).TotalSeconds / range.TotalSeconds * buckets);
            if (i < 0 || i >= buckets) continue;
            sums[i] += v;
            counts[i]++;
            maxes[i] = double.IsNaN(maxes[i]) ? v : Math.Max(maxes[i], v);
        }
        for (int i = 0; i < buckets; i++)
            result[i] = counts[i] == 0 ? double.NaN : max ? maxes[i] : sums[i] / counts[i];
        return result;
    }
}
