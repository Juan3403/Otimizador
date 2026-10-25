using System.Diagnostics;
using System.Text;

namespace Otimizador.App;

/// <summary>Executa um script do PowerShell (sem se preocupar com aspas) e devolve o texto de saída.</summary>
public static class PsRunner
{
    public static string? Run(string script, int timeoutMs)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8\n" + script));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var err = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { /* já terminou */ }
                return null;
            }
            _ = err.Result;
            return output;
        }
        catch
        {
            return null;
        }
    }
}

