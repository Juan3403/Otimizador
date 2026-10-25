using System.Diagnostics;

namespace Otimizador.App.Tweaks;

public static class TweakExecutor
{
    public enum RestoreResult { Created, RecentExists, Failed }

    /// <summary>
    /// Garante uma proteção antes dos ajustes. Tenta criar um ponto de restauração e depois CONFIRMA no Windows que ele existe
    /// (o Checkpoint-Computer pode terminar sem erro mesmo sem criar nada, ex.: Proteção do Sistema desligada ou limite de 24 h).
    /// </summary>
    public static RestoreResult EnsureRestorePoint()
    {
        if (!AppServices.IsAdmin) return RestoreResult.Failed;
        RunPowerShell("Checkpoint-Computer -Description 'Otimizador' -RestorePointType MODIFY_SETTINGS -ErrorAction SilentlyContinue", 120_000);

        var age = LatestRestorePointAge();
        if (age is null) return RestoreResult.Failed;
        return age.Value < TimeSpan.FromMinutes(10) ? RestoreResult.Created
             : age.Value < TimeSpan.FromHours(24) ? RestoreResult.RecentExists
             : RestoreResult.Failed;
    }

    /// <summary>Idade do ponto de restauração mais recente (null se não houver nenhum ou se não for possível consultar).</summary>
    public static TimeSpan? LatestRestorePointAge()
    {
        var output = RunPowerShell(
            "$r = Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Sort-Object SequenceNumber -Descending | Select-Object -First 1; " +
            "if ($r) { [System.Management.ManagementDateTimeConverter]::ToDateTime($r.CreationTime).ToString('o') }", 60_000);
        return DateTime.TryParse(output?.Trim(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var created)
            ? DateTime.Now - created
            : null;
    }

    public static string? RunPowerShell(string command, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{command}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stderr = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(timeoutMs);
            _ = stderr.Result;
            return output;
        }
        catch
        {
            return null;
        }
    }
    public static List<TweakResult> Apply(IEnumerable<Tweak> tweaks) => Run(tweaks, apply: true);

    public static List<TweakResult> Revert(IEnumerable<Tweak> tweaks) => Run(tweaks, apply: false);

    private static List<TweakResult> Run(IEnumerable<Tweak> tweaks, bool apply)
    {
        var store = TweakStateStore.Load();
        var results = new List<TweakResult>();
        foreach (var t in tweaks)
        {
            if (t.NeedsAdmin && !AppServices.IsAdmin)
            {
                results.Add(new(t, TweakOutcome.SkippedNeedsAdmin));
                continue;
            }
            try
            {
                if (apply)
                {
                    t.Apply(store);
                    results.Add(new(t, TweakOutcome.Applied));
                }
                else
                {
                    results.Add(new(t, t.Revert(store) ? TweakOutcome.Reverted : TweakOutcome.NothingToUndo));
                }
            }
            catch (Exception ex)
            {
                results.Add(new(t, TweakOutcome.Error, ex.Message));
            }
        }
        return results;
    }

    /// <summary>Apaga arquivos temporários com mais de 1 dia. Retorna os bytes liberados.</summary>
    public static long CleanTemp()
    {
        long freed = 0;
        var limit = DateTime.UtcNow.AddDays(-1);
        var root = Path.GetTempPath();
        foreach (var file in SafeFiles(root))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc > limit) continue;
                var size = info.Length;
                info.Delete();
                freed += size;
            }
            catch
            {
                // arquivo em uso: ignora
            }
        }
        return freed;
    }

    private static IEnumerable<string> SafeFiles(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateFiles(root, "*", options);
    }
}



