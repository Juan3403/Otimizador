using LibreHardwareMonitor.Hardware;

namespace Otimizador.App.Hardware;

public sealed record SensorReading(
    string Id, string Hardware, HardwareType HardwareType, SensorType Type, string Name, float Value);

/// <summary>Leitura de sensores (temperatura, uso, clock, potência, ventoinhas) via LibreHardwareMonitor.</summary>
public sealed class SensorService : IDisposable
{
    private readonly object _lock = new();
    private Computer? _computer;
    private bool _failed;

    private static readonly SensorType[] Wanted =
        { SensorType.Temperature, SensorType.Load, SensorType.Clock, SensorType.Power, SensorType.Fan, SensorType.Data };

    /// <summary>Driver PawnIO, necessário para ler a temperatura de CPUs AMD Ryzen e algumas Intel.</summary>
    public static bool PawnIoInstalled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private bool EnsureOpen()
    {
        if (_computer is not null) return true;
        if (_failed) return false;
        try
        {
            var c = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
            };
            c.Open();
            _computer = c;
            return true;
        }
        catch
        {
            _failed = true;
            return false;
        }
    }

    /// <summary>Fecha e reabre a leitura (depois de instalar o driver, para as temperaturas aparecerem sem reiniciar o app).</summary>
    public void Reopen()
    {
        lock (_lock)
        {
            try { _computer?.Close(); } catch { /* já estava fechado */ }
            _computer = null;
            _failed = false;
        }
    }

    public List<SensorReading> Snapshot()
    {
        lock (_lock)
        {
            var result = new List<SensorReading>();
            if (!EnsureOpen() || _computer is null) return result;
            foreach (var hw in _computer.Hardware)
                Collect(hw, result);
            return result;
        }
    }

    private static void Collect(IHardware hw, List<SensorReading> result)
    {
        try
        {
            hw.Update();
        }
        catch
        {
            return;
        }
        foreach (var s in hw.Sensors)
        {
            if (s.Value is null || !Wanted.Contains(s.SensorType)) continue;
            // "Data" (GB) só interessa para a memória; nos discos há contadores acumulados sem utilidade aqui.
            if (s.SensorType == SensorType.Data && hw.HardwareType != HardwareType.Memory) continue;
            result.Add(new SensorReading(s.Identifier.ToString(), hw.Name, hw.HardwareType, s.SensorType, s.Name, s.Value.Value));
        }
        foreach (var sub in hw.SubHardware)
            Collect(sub, result);
    }

    public static string Unit(SensorType t) => t switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Clock => "MHz",
        SensorType.Power => "W",
        SensorType.Fan => "RPM",
        SensorType.Data => "GB",
        _ => "",
    };

    private static bool IsGpu(HardwareType t) =>
        t is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static IEnumerable<SensorReading> Of(IEnumerable<SensorReading> s, Func<HardwareType, bool> hw, SensorType type) =>
        s.Where(r => hw(r.HardwareType) && r.Type == type);

    /// <summary>Temperatura principal da CPU (null se indisponível, ex.: sem administrador).</summary>
    public static float? CpuTemp(IEnumerable<SensorReading> s)
    {
        // Sem driver/administrador alguns chips devolvem 0 °C: tratamos como indisponível.
        var temps = Of(s, h => h == HardwareType.Cpu, SensorType.Temperature).Where(r => r.Value > 0).ToList();
        foreach (var pref in new[] { "Package", "Average", "Tctl", "Tdie", "Core Max" })
        {
            var m = temps.FirstOrDefault(r => r.Name.Contains(pref, StringComparison.OrdinalIgnoreCase));
            if (m is not null) return m.Value;
        }
        return temps.Count > 0 ? temps.Max(r => r.Value) : null;
    }

    public static float? CpuLoad(IEnumerable<SensorReading> s) =>
        Of(s, h => h == HardwareType.Cpu, SensorType.Load).FirstOrDefault(r => r.Name == "CPU Total")?.Value;

    /// <summary>Clock médio dos núcleos (MHz).</summary>
    public static float? CpuClock(IEnumerable<SensorReading> s)
    {
        var clocks = Of(s, h => h == HardwareType.Cpu, SensorType.Clock)
            .Where(r => r.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && r.Value > 0).ToList();
        return clocks.Count > 0 ? clocks.Average(r => r.Value) : null;
    }

    public static float? CpuPower(IEnumerable<SensorReading> s)
    {
        var p = Of(s, h => h == HardwareType.Cpu, SensorType.Power).ToList();
        return p.FirstOrDefault(r => r.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))?.Value
               ?? (p.Count > 0 ? p.Max(r => r.Value) : null);
    }

    public static float? GpuTemp(IEnumerable<SensorReading> s) =>
        Of(s, IsGpu, SensorType.Temperature).FirstOrDefault(r => r.Name.Contains("Core"))?.Value
        ?? Of(s, IsGpu, SensorType.Temperature).FirstOrDefault()?.Value;

    public static float? GpuLoad(IEnumerable<SensorReading> s) =>
        Of(s, IsGpu, SensorType.Load).FirstOrDefault(r => r.Name == "GPU Core")?.Value
        ?? Of(s, IsGpu, SensorType.Load).FirstOrDefault()?.Value;

    public static float? GpuClock(IEnumerable<SensorReading> s) =>
        Of(s, IsGpu, SensorType.Clock).FirstOrDefault(r => r.Name.Contains("Core"))?.Value
        ?? Of(s, IsGpu, SensorType.Clock).FirstOrDefault()?.Value;

    public static float? GpuPower(IEnumerable<SensorReading> s)
    {
        var p = Of(s, IsGpu, SensorType.Power).ToList();
        return p.FirstOrDefault(r => r.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                                     || r.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))?.Value
               ?? (p.Count > 0 ? p.Max(r => r.Value) : null);
    }

    public static float? RamLoad(IEnumerable<SensorReading> s) =>
        Of(s, h => h == HardwareType.Memory, SensorType.Load).FirstOrDefault(r => r.Name == "Memory")?.Value;

    public static float? RamUsedGb(IEnumerable<SensorReading> s) =>
        Of(s, h => h == HardwareType.Memory, SensorType.Data).FirstOrDefault(r => r.Name == "Memory Used")?.Value;

    public static float? RamTotalGb(IEnumerable<SensorReading> s)
    {
        var used = RamUsedGb(s);
        var free = Of(s, h => h == HardwareType.Memory, SensorType.Data).FirstOrDefault(r => r.Name == "Memory Available")?.Value;
        return used is not null && free is not null ? used + free : null;
    }

    /// <summary>Espaço usado (%) do primeiro disco com leitura.</summary>
    public static float? DiskUsed(IEnumerable<SensorReading> s) =>
        Of(s, h => h == HardwareType.Storage, SensorType.Load).FirstOrDefault(r => r.Name == "Used Space")?.Value;

    public static float? DiskTemp(IEnumerable<SensorReading> s) =>
        Of(s, h => h == HardwareType.Storage, SensorType.Temperature).FirstOrDefault(r => r.Name == "Temperature")?.Value
        ?? Of(s, h => h == HardwareType.Storage, SensorType.Temperature).FirstOrDefault()?.Value;

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                _computer?.Close();
            }
            catch
            {
                // encerrando o app
            }
            _computer = null;
        }
    }
}
