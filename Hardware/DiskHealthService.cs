using System.Management;

namespace Otimizador.App.Hardware;

/// <summary>0 = ótimo, 1 = atenção, 2 = problema, 3 = sem dados.</summary>
public enum HealthLevel { Good = 0, Warning = 1, Bad = 2, Unknown = 3 }

public sealed class DiskHealth
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required long SizeBytes { get; init; }
    public HealthLevel Level { get; init; } = HealthLevel.Unknown;
    public string Verdict { get; init; } = "";
    public double? TemperatureC { get; init; }
    public double? WearPercent { get; init; }
    public double? PowerOnHours { get; init; }
    public long? ReadErrors { get; init; }
    public long? WriteErrors { get; init; }
    public string Details { get; init; } = "";
}

/// <summary>Saúde dos discos pelo próprio Windows (Storage Management): estado geral, desgaste, temperatura e erros.</summary>
public static class DiskHealthService
{
    private const string Scope = @"root\Microsoft\Windows\Storage";

    public static List<DiskHealth> Read()
    {
        var result = new List<DiskHealth>();
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM MSFT_PhysicalDisk");
            foreach (ManagementObject disk in searcher.Get())
            {
                try
                {
                    result.Add(Describe(disk));
                }
                catch
                {
                    // disco sem informações
                }
            }
        }
        catch
        {
            // Storage Management indisponível
        }
        return result.OrderBy(d => d.Name).ToList();
    }

    private static T? Get<T>(ManagementBaseObject o, string name) where T : struct
    {
        try
        {
            var v = o[name];
            return v is null ? null : (T)Convert.ChangeType(v, typeof(T));
        }
        catch
        {
            return null;
        }
    }

    private static DiskHealth Describe(ManagementObject disk)
    {
        string name = disk["FriendlyName"]?.ToString()?.Trim() ?? "Disco";
        long size = Get<long>(disk, "Size") ?? 0;
        int media = Get<int>(disk, "MediaType") ?? 0;       // 3 = HDD, 4 = SSD, 5 = SCM
        int bus = Get<int>(disk, "BusType") ?? 0;           // 17 = NVMe, 11 = SATA
        int health = Get<int>(disk, "HealthStatus") ?? 5;   // 0 = Healthy, 1 = Warning, 2 = Unhealthy

        string kind = (media, bus) switch
        {
            (_, 17) => "NVMe SSD",
            (4, _) => "SSD",
            (3, _) => "HDD",
            _ => Loc.T("Disco", "Disk"),
        };

        double? temp = null, wear = null, hours = null;
        long? readErr = null, writeErr = null;
        try
        {
            using var rel = disk.GetRelated("MSFT_StorageReliabilityCounter");
            foreach (ManagementObject c in rel)
            {
                temp = Get<double>(c, "Temperature");
                wear = Get<double>(c, "Wear");
                hours = Get<double>(c, "PowerOnHours");
                readErr = Get<long>(c, "ReadErrorsTotal");
                writeErr = Get<long>(c, "WriteErrorsTotal");
                break;
            }
        }
        catch
        {
            // contadores exigem administrador em alguns discos
        }

        var level = health switch { 0 => HealthLevel.Good, 1 => HealthLevel.Warning, 2 => HealthLevel.Bad, _ => HealthLevel.Unknown };
        var notes = new List<string>();
        if (wear is >= 90) { level = HealthLevel.Bad; notes.Add(Loc.T("desgaste muito alto: troque o disco em breve", "very high wear: replace the drive soon")); }
        else if (wear is >= 70) { level = (HealthLevel)Math.Max((int)level, (int)HealthLevel.Warning); notes.Add(Loc.T("desgaste alto", "high wear")); }
        if (temp is >= 70) { level = (HealthLevel)Math.Max((int)level, (int)HealthLevel.Warning); notes.Add(Loc.T("temperatura alta", "high temperature")); }
        if (readErr is > 0 || writeErr is > 0) { level = (HealthLevel)Math.Max((int)level, (int)HealthLevel.Warning); notes.Add(Loc.T("houve erros de leitura/escrita", "read/write errors occurred")); }

        string verdict = level switch
        {
            HealthLevel.Good => Loc.T("Saudável", "Healthy"),
            HealthLevel.Warning => Loc.T("Atenção", "Warning"),
            HealthLevel.Bad => Loc.T("Problema", "Problem"),
            _ => Loc.T("Sem dados", "No data"),
        };

        return new DiskHealth
        {
            Name = name, Kind = kind, SizeBytes = size, Level = level, Verdict = verdict,
            TemperatureC = temp is > 0 ? temp : null, WearPercent = wear, PowerOnHours = hours,
            ReadErrors = readErr, WriteErrors = writeErr, Details = string.Join(" · ", notes),
        };
    }
}
