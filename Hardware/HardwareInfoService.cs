using System.Management;
using System.Text;
using Microsoft.Win32;

namespace Otimizador.App.Hardware;

public sealed record InfoLine(string Label, string Value);

public sealed record InfoSection(string Key, string Title, IReadOnlyList<InfoLine> Lines);

/// <summary>Inventário estático do hardware via WMI/CIM e registro.</summary>
public static class HardwareInfoService
{
    private static Task<List<InfoSection>>? _cache;

    /// <summary>Inventário em cache (a leitura por WMI leva ~1 s). Use force para reler.</summary>
    public static Task<List<InfoSection>> GetAsync(bool force = false)
    {
        if (force || _cache is null) _cache = Task.Run(Collect);
        return _cache;
    }

    /// <summary>Primeira linha (nome/total) de uma seção, para resumos.</summary>
    public static string? Headline(IEnumerable<InfoSection> sections, string key) =>
        sections.FirstOrDefault(s => s.Key == key)?.Lines.FirstOrDefault()?.Value;

    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection>
        {
            Safe("system", "Sec_System", SystemInfo),
            Safe("cpu", "Sec_Cpu", Cpu),
            Safe("memory", "Sec_Memory", Memory),
            Safe("gpu", "Sec_Gpu", Gpu),
            Safe("board", "Sec_Board", Board),
            Safe("disks", "Sec_Disks", Disks),
            Safe("battery", "Sec_Battery", Battery),
        };
        return sections;
    }

    public static string ToText(IEnumerable<InfoSection> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Otimizador - {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var s in sections)
        {
            sb.AppendLine().AppendLine($"== {s.Title} ==");
            foreach (var l in s.Lines)
                sb.AppendLine($"{l.Label}: {l.Value}");
        }
        return sb.ToString();
    }

    private static InfoSection Safe(string key, string titleKey, Func<List<InfoLine>> collect)
    {
        var title = Loc.T(titleKey);
        try
        {
            return new InfoSection(key, title, collect());
        }
        catch (Exception ex)
        {
            return new InfoSection(key, title, new List<InfoLine> { new(Loc.T("L_Status"), ex.Message) });
        }
    }

    private static List<ManagementBaseObject> Query(string wql)
    {
        using var searcher = new ManagementObjectSearcher(wql);
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }

    private static string Str(ManagementBaseObject o, string prop)
    {
        var v = o[prop];
        var s = v?.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? Loc.T("L_Unknown") : s;
    }

    private static ulong U64(ManagementBaseObject o, string prop) =>
        o[prop] is null ? 0 : Convert.ToUInt64(o[prop]);

    public static string FormatBytes(ulong bytes)
    {
        double gb = bytes / 1024d / 1024d / 1024d;
        return gb >= 1 ? $"{gb:0.#} GB" : $"{bytes / 1024d / 1024d:0} MB";
    }

    private static List<InfoLine> SystemInfo()
    {
        var os = Query("SELECT Caption, Version, OSArchitecture, CSName FROM Win32_OperatingSystem").First();
        return new List<InfoLine>
        {
            new(Loc.T("L_Os"), Str(os, "Caption")),
            new(Loc.T("L_Version"), Str(os, "Version")),
            new(Loc.T("L_Arch"), Str(os, "OSArchitecture")),
            new(Loc.T("L_Machine"), Str(os, "CSName")),
        };
    }

    private static List<InfoLine> Cpu()
    {
        var lines = new List<InfoLine>();
        foreach (var c in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor"))
        {
            lines.Add(new(Loc.T("L_Name"), Str(c, "Name")));
            lines.Add(new(Loc.T("L_Cores"), Str(c, "NumberOfCores")));
            lines.Add(new(Loc.T("L_Threads"), Str(c, "NumberOfLogicalProcessors")));
            lines.Add(new(Loc.T("L_Clock"), $"{Str(c, "MaxClockSpeed")} MHz"));
            lines.Add(new(Loc.T("L_L2"), $"{U64(c, "L2CacheSize")} KB"));
            lines.Add(new(Loc.T("L_L3"), $"{U64(c, "L3CacheSize")} KB"));
        }
        return lines;
    }

    private static List<InfoLine> Memory()
    {
        var lines = new List<InfoLine>();
        ulong total = 0;
        int index = 0;
        foreach (var m in Query("SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, DeviceLocator FROM Win32_PhysicalMemory"))
        {
            index++;
            var cap = U64(m, "Capacity");
            total += cap;
            var speed = U64(m, "ConfiguredClockSpeed");
            if (speed == 0) speed = U64(m, "Speed");
            lines.Add(new($"{Loc.T("L_Module")} {index} ({Str(m, "DeviceLocator")})",
                $"{FormatBytes(cap)} · {speed} MHz · {Str(m, "Manufacturer")} {Str(m, "PartNumber")}"));
        }
        lines.Insert(0, new(Loc.T("L_Total"), FormatBytes(total)));
        return lines;
    }

    private static List<InfoLine> Gpu()
    {
        var lines = new List<InfoLine>();
        foreach (var g in Query("SELECT Name, AdapterRAM, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
        {
            var name = Str(g, "Name");
            lines.Add(new(Loc.T("L_Name"), name));
            var vram = VideoMemoryFromRegistry(name) ?? U64(g, "AdapterRAM");
            lines.Add(new(Loc.T("L_Vram"), vram > 0 ? FormatBytes(vram) : Loc.T("L_Unknown")));
            lines.Add(new(Loc.T("L_Driver"), Str(g, "DriverVersion")));
            lines.Add(new(Loc.T("L_Resolution"),
                $"{Str(g, "CurrentHorizontalResolution")}x{Str(g, "CurrentVerticalResolution")} @ {Str(g, "CurrentRefreshRate")} Hz"));
        }
        return lines;
    }

    // AdapterRAM do WMI é limitado a 4 GB; o registro guarda o valor real de 64 bits.
    private static ulong? VideoMemoryFromRegistry(string adapterName)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return null;
            foreach (var sub in cls.GetSubKeyNames())
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") as string != adapterName) continue;
                return k.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => (ulong)l,
                    byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
                    _ => null,
                };
            }
        }
        catch
        {
            // sem acesso: usa o valor do WMI
        }
        return null;
    }

    private static List<InfoLine> Board()
    {
        var b = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").First();
        var bios = Query("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS").First();
        return new List<InfoLine>
        {
            new(Loc.T("L_Maker"), Str(b, "Manufacturer")),
            new(Loc.T("L_Model"), Str(b, "Product")),
            new(Loc.T("L_Bios"), $"{Str(bios, "Manufacturer")} {Str(bios, "SMBIOSBIOSVersion")}"),
        };
    }

    private static List<InfoLine> Disks()
    {
        var lines = new List<InfoLine>();
        foreach (var d in Query("SELECT Model, Size, InterfaceType, MediaType, Status FROM Win32_DiskDrive"))
        {
            lines.Add(new(Str(d, "Model"),
                $"{FormatBytes(U64(d, "Size"))} · {Str(d, "InterfaceType")} · {Str(d, "MediaType")} · {Str(d, "Status")}"));
        }
        return lines;
    }

    private static List<InfoLine> Battery()
    {
        var list = Query("SELECT Name, EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
        if (list.Count == 0)
            return new List<InfoLine> { new(Loc.T("L_Status"), Loc.T("L_None")) };
        return list.Select(b => new InfoLine(Str(b, "Name"), $"{Str(b, "EstimatedChargeRemaining")} %")).ToList();
    }
}

