using System.Text.Json;

namespace Otimizador.App.Bench;

/// <summary>Guarda o último resultado de cada teste para comparação.</summary>
public static class BenchHistory
{
    private static string FilePath => Path.Combine(AppServices.DataDir, "bench-history.json");

    public static Dictionary<string, double> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Save(IEnumerable<BenchResult> results)
    {
        try
        {
            var all = Load();
            foreach (var r in results) all[r.Key] = r.Value;
            Directory.CreateDirectory(AppServices.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch
        {
            // histórico é opcional
        }
    }
}
