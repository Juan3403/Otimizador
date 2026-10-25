using System.Management;

namespace Otimizador.App.Hardware;

public sealed record BatteryHealth(
    double? DesignedMWh, double? FullChargeMWh, int? Cycles, int? ChargePercent, double? HealthPercent, HealthLevel Level, string Verdict);

/// <summary>Saúde da bateria de notebooks: capacidade atual comparada com a de fábrica e ciclos de carga.</summary>
public static class BatteryHealthService
{
    private static T? One<T>(string cls, string prop) where T : struct
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", $"SELECT {prop} FROM {cls}");
            foreach (ManagementBaseObject o in s.Get())
                return o[prop] is null ? null : (T)Convert.ChangeType(o[prop], typeof(T));
        }
        catch
        {
            // classe indisponível: PC sem bateria ou driver sem suporte
        }
        return null;
    }

    /// <summary>Null se o PC não tem bateria.</summary>
    public static BatteryHealth? Read()
    {
        int? charge = null;
        bool present = false;
        try
        {
            using var s = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
            foreach (ManagementBaseObject o in s.Get())
            {
                present = true;
                charge = o["EstimatedChargeRemaining"] is { } c ? Convert.ToInt32(c) : null;
                break;
            }
        }
        catch
        {
            return null;
        }
        if (!present) return null;

        double? design = One<uint>("BatteryStaticData", "DesignedCapacity");
        double? full = One<uint>("BatteryFullChargedCapacity", "FullChargedCapacity");
        int? cycles = One<uint>("BatteryCycleCount", "CycleCount") is { } cc ? (int)cc : null;

        double? health = design is > 0 && full is > 0 ? Math.Min(100, full.Value / design.Value * 100) : null;
        var level = health switch { null => HealthLevel.Unknown, >= 80 => HealthLevel.Good, >= 60 => HealthLevel.Warning, _ => HealthLevel.Bad };
        string verdict = level switch
        {
            HealthLevel.Good => Loc.T("Boa", "Good"),
            HealthLevel.Warning => Loc.T("Desgastada", "Worn"),
            HealthLevel.Bad => Loc.T("Trocar em breve", "Replace soon"),
            _ => Loc.T("Sem dados de capacidade", "No capacity data"),
        };
        return new BatteryHealth(design, full, cycles, charge, health, level, verdict);
    }
}
