using System.Diagnostics;

namespace Otimizador.App.Tray;

/// <summary>
/// Abrir com o Windows. O app pede administrador, e um programa assim não abre sozinho pela chave "Run" (o UAC bloqueia);
/// por isso usamos uma tarefa agendada de logon com privilégios elevados, que abre sem perguntar nada.
/// </summary>
public static class LogonTask
{
    private const string TaskName = "Otimizador";

    private static (int Code, string Output) Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "");
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(20_000);
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public static bool Exists() => Run($"/Query /TN \"{TaskName}\"").Code == 0;

    /// <summary>Cria ou remove a tarefa. Retorna false se não deu (ex.: sem administrador).</summary>
    public static bool Set(bool enabled)
    {
        if (!enabled) return Run($"/Delete /TN \"{TaskName}\" /F").Code == 0 || !Exists();
        var exe = Environment.ProcessPath!;
        return Run($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F").Code == 0;
    }
}
