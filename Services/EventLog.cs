using System.Globalization;

namespace Otimizador.App;

public sealed record AppEvent(DateTime Time, string Kind, string Text);

/// <summary>Diário simples do que o app fez (ajustes aplicados, limpezas, testes, alertas), usado na aba Histórico.</summary>
public static class EventLog
{
    private static readonly object Lock = new();

    private static string FilePath => Path.Combine(AppServices.DataDir, "events.csv");

    public static void Add(string kind, string text)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(AppServices.DataDir);
                var clean = text.Replace("\r", " ").Replace("\n", " ").Replace(";", ",");
                File.AppendAllText(FilePath, $"{DateTime.Now:s};{kind};{clean}{Environment.NewLine}");
            }
        }
        catch
        {
            // o diário é opcional
        }
    }

    public static List<AppEvent> Read(DateTime since, int max = 200)
    {
        var list = new List<AppEvent>();
        try
        {
            lock (Lock)
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadLines(FilePath))
                {
                    var p = line.Split(';', 3);
                    if (p.Length < 3 || !DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) || t < since) continue;
                    list.Add(new AppEvent(t, p[1], p[2]));
                }
            }
        }
        catch
        {
            // arquivo em uso
        }
        return list.OrderByDescending(e => e.Time).Take(max).ToList();
    }
}
