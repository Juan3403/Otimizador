using System.Diagnostics;
using System.Security.Principal;
using Otimizador.App.Hardware;

namespace Otimizador.App;

public static class AppServices
{
    public static SensorService Sensors { get; } = new();

    /// <summary>Versão portátil: se existir "portable.txt" ao lado do programa, os dados ficam na pasta "Dados" dele.</summary>
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    public static string DataDir { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "Dados")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Otimizador");

    public static bool IsAdmin { get; } = CheckAdmin();

    private static bool CheckAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Reabre o app com UAC. Retorna false se o usuário recusar.</summary>
    public static bool RelaunchAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas",
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
