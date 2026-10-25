using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Otimizador.App.Tweaks;

/// <summary>Planos de energia do Windows (powercfg).</summary>
public static class PowerPlans
{
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public static string? Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(15_000);
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? ParseGuid(string? text)
    {
        if (text is null) return null;
        var m = Regex.Match(text, "[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    public static string? Active() => ParseGuid(Run("/getactivescheme"));

    public static bool Set(string guid) => Run($"/setactive {guid}") is not null;

    /// <summary>Ativa o plano Alto desempenho (criando uma cópia se o PC não o oferece). Retorna o GUID ativado ou null.</summary>
    public static string? ActivateHighPerformance()
    {
        if (Set(HighPerformance)) return HighPerformance;
        var copy = ParseGuid(Run($"/duplicatescheme {HighPerformance}"));
        return copy is not null && Set(copy) ? copy : null;
    }
}
