using System.Diagnostics;
using System.Security.Cryptography;
using Otimizador.App.Dialogs;

namespace Otimizador.App.Hardware;

/// <summary>
/// O driver PawnIO (GPL-2.0, de namazso) é o que deixa o programa ler a temperatura de CPUs AMD Ryzen e algumas Intel.
/// O instalador oficial dele vai dentro do pacote (pasta Drivers), sem alteração; aqui ele é conferido (SHA-256 fixo) e executado.
/// </summary>
public static class PawnIoInstaller
{
    /// <summary>SHA-256 do PawnIO_setup.exe 2.2.0 publicado em github.com/namazso/PawnIO.Setup.</summary>
    public const string ExpectedSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    public static string SetupPath => Path.Combine(AppContext.BaseDirectory, "Drivers", "PawnIO_setup.exe");

    /// <summary>O arquivo é o oficial, sem alteração?</summary>
    public static bool IsAuthentic(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs)).Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Dá para instalar agora: sem o driver, com o instalador dele no pacote e com o app como administrador.</summary>
    public static bool CanInstall => AppServices.IsAdmin && !SensorService.PawnIoInstalled() && File.Exists(SetupPath);

    public sealed record Result(bool Ok, string Message);

    /// <summary>Roda o instalador do PawnIO em modo silencioso (precisa de administrador, que o app já tem).</summary>
    public static Result Install()
    {
        if (SensorService.PawnIoInstalled()) return new Result(true, Loc.T("PawnIo_AlreadyInstalled"));
        if (!File.Exists(SetupPath)) return new Result(false, Loc.T("PawnIo_Missing"));
        if (!IsAuthentic(SetupPath)) return new Result(false, Loc.T("PawnIo_NotAuthentic"));
        try
        {
            using var p = Process.Start(new ProcessStartInfo(SetupPath, "-install -silent") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (p is null) return new Result(false, Loc.T("PawnIo_Failed"));
            if (!p.WaitForExit(120_000)) return new Result(false, Loc.T("PawnIo_Failed"));
            // 3010 = instalou, pede reinício; o driver já funciona sem reiniciar na maioria dos casos
            bool ok = (p.ExitCode == 0 || p.ExitCode == 3010) && SensorService.PawnIoInstalled();
            if (ok) EventLog.Add("driver", "Driver PawnIO instalado (temperatura da CPU)");
            return ok ? new Result(true, Loc.T("PawnIo_Done")) : new Result(false, $"{Loc.T("PawnIo_Failed")} ({p.ExitCode})");
        }
        catch (Exception ex)
        {
            return new Result(false, $"{Loc.T("PawnIo_Failed")} ({ex.Message})");
        }
    }

    /// <summary>Instala e faz os sensores serem lidos de novo, para a temperatura aparecer sem reabrir o programa.</summary>
    public static async Task<Result> InstallAndRefreshAsync()
    {
        var result = await Task.Run(Install);
        if (result.Ok) AppServices.Sensors.Reopen();
        return result;
    }

    /// <summary>Na primeira vez que a temperatura da CPU não aparece e o driver está no pacote, oferece instalar (uma única vez).</summary>
    public static void StartFirstRunPrompt()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            var settings = Settings.Load();
            if (settings.PawnIoAsked || !CanInstall) return;
            if (System.Windows.Application.Current?.MainWindow is not { IsVisible: true }) return; // aberto na bandeja: pergunta quando a janela abrir
            var snap = await Task.Run(() => AppServices.Sensors.Snapshot());
            if (SensorService.CpuTemp(snap) is not null) return; // a temperatura já funciona: não precisa do driver

            settings.PawnIoAsked = true;
            settings.Save();
            if (!ConfirmDialog.Confirm(Loc.T("PawnIo_PromptTitle"), Loc.T("PawnIo_PromptText"), null, Loc.T("PawnIo_PromptYes"))) return;
            var result = await InstallAndRefreshAsync();
            Alerts.ToastWindow.Show(Loc.T("PawnIo_PromptTitle"), result.Message);
        };
        timer.Start();
    }
}
