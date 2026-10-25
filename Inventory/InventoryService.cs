using System.Text.Json;
using System.Windows.Threading;
using Otimizador.App.Boot;
using Otimizador.App.Hardware;
using Otimizador.App.Security;

namespace Otimizador.App.Inventory;

public sealed class InventoryDisk
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public double SizeGb { get; set; }
    public string Health { get; set; } = "";
    public int HealthLevel { get; set; }
}

/// <summary>Resumo de um computador, gravado como um arquivo .json por máquina numa pasta compartilhada.</summary>
public sealed class InventoryRecord
{
    public string Machine { get; set; } = "";
    public string User { get; set; } = "";
    public string Os { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string Gpu { get; set; } = "";
    public double RamGb { get; set; }
    public string AppVersion { get; set; } = "";
    public DateTime Time { get; set; }
    public int StartupEnabled { get; set; }
    public int StartupTotal { get; set; }
    public int? SecurityGood { get; set; }
    public int? SecurityTotal { get; set; }
    public List<InventoryDisk> Disks { get; set; } = new();

    /// <summary>0 = tudo bem, 1 = atenção, 2 = problema (pior caso entre discos e segurança).</summary>
    public int Level
    {
        get
        {
            int level = Disks.Select(d => d.HealthLevel is 3 ? 0 : d.HealthLevel).DefaultIfEmpty(0).Max();
            if (SecurityTotal is > 0 && SecurityGood is { } g)
            {
                double ratio = (double)g / SecurityTotal.Value;
                level = Math.Max(level, ratio >= 0.85 ? 0 : ratio >= 0.6 ? 1 : 2);
            }
            return level;
        }
    }
}

/// <summary>Inventário central (desligado por padrão): cada PC publica seu resumo numa pasta que você escolhe (ex.: rede da empresa).</summary>
public static class InventoryService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromHours(6) };
    private static bool _started;

    public static async Task<InventoryRecord> BuildAsync()
    {
        var sections = await HardwareInfoService.GetAsync();
        var disks = await Task.Run(DiskHealthService.Read);
        var startup = await Task.Run(StartupService.List);
        double ram = 0;
        var ramText = HardwareInfoService.Headline(sections, "memory");
        if (ramText is not null && double.TryParse(ramText.Split(' ')[0].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gb)) ram = gb;
        var sec = SecurityService.Last is { } last ? SecurityService.Score(last) : ((int, int)?)null;

        return new InventoryRecord
        {
            Machine = Environment.MachineName,
            User = Environment.UserName,
            Os = HardwareInfoService.Headline(sections, "system") ?? "",
            Cpu = HardwareInfoService.Headline(sections, "cpu") ?? "",
            Gpu = HardwareInfoService.Headline(sections, "gpu") ?? "",
            RamGb = ram,
            AppVersion = AppInfo.VersionText,
            Time = DateTime.Now,
            StartupEnabled = startup.Count(s => s.Enabled),
            StartupTotal = startup.Count,
            SecurityGood = sec?.Item1,
            SecurityTotal = sec?.Item2,
            Disks = disks.Select(d => new InventoryDisk
            {
                Name = d.Name, Kind = d.Kind, SizeGb = Math.Round(d.SizeBytes / 1073741824.0, 1), Health = d.Verdict, HealthLevel = (int)d.Level,
            }).ToList(),
        };
    }

    /// <summary>Grava o resumo deste PC na pasta. Retorna (ok, mensagem).</summary>
    public static async Task<(bool Ok, string Message)> PublishAsync(string? folder = null)
    {
        folder ??= Settings.Load().InventoryFolder;
        if (string.IsNullOrWhiteSpace(folder)) return (false, Loc.T("Escolha a pasta do inventário.", "Choose the inventory folder."));
        try
        {
            Directory.CreateDirectory(folder);
            var record = await BuildAsync();
            var path = Path.Combine(folder, SafeName(record.Machine) + ".json");
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(record, Json));
            File.Move(tmp, path, overwrite: true);
            return (true, path);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static List<InventoryRecord> ReadAll(string? folder)
    {
        var list = new List<InventoryRecord>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize<InventoryRecord>(File.ReadAllText(file));
                if (r is not null && !string.IsNullOrWhiteSpace(r.Machine)) list.Add(r);
            }
            catch
            {
                // arquivo incompleto ou de outro programa
            }
        }
        return list.OrderBy(r => r.Machine, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>Publica depois de 40 s e a cada 6 h, se o inventário estiver ligado.</summary>
    public static void Start()
    {
        if (_started) return;
        _started = true;
        Timer.Tick += async (_, _) => await TickAsync();
        Timer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(40) };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            await TickAsync();
        };
        first.Start();
    }

    private static async Task TickAsync()
    {
        var s = Settings.Load();
        if (!s.InventoryEnabled || string.IsNullOrWhiteSpace(s.InventoryFolder)) return;
        await PublishAsync(s.InventoryFolder);
    }
}
