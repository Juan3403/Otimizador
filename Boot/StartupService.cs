using System.Diagnostics;
using Microsoft.Win32;

namespace Otimizador.App.Boot;

public sealed class StartupEntry
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Source { get; init; }
    public string Publisher { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Enabled { get; set; }

    public required RegistryHive ApprovedHive { get; init; }
    public required string ApprovedSubKey { get; init; }
    public required string ValueName { get; init; }
}

/// <summary>
/// Lista e liga/desliga os programas que abrem com o Windows, do mesmo jeito que o Gerenciador de Tarefas:
/// guarda o estado em StartupApproved (nada é apagado, então dá para religar a qualquer momento).
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        AddRegistry(list, RegistryHive.CurrentUser, RunKey, "Run", Loc.T("Usuário (registro)", "User (registry)"));
        AddRegistry(list, RegistryHive.LocalMachine, RunKey, "Run", Loc.T("Todos os usuários (registro)", "All users (registry)"));
        AddRegistry(list, RegistryHive.LocalMachine, Run32Key, "Run32", Loc.T("Todos os usuários, 32 bits (registro)", "All users, 32-bit (registry)"));
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser,
            Loc.T("Pasta Inicializar (usuário)", "Startup folder (user)"));
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine,
            Loc.T("Pasta Inicializar (todos)", "Startup folder (all users)"));
        return list.OrderByDescending(e => e.Enabled).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void AddRegistry(List<StartupEntry> list, RegistryHive hive, string path, string approvedKey, string source)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                var command = key.GetValue(name)?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(command)) continue;
                var (publisher, description) = Describe(command);
                list.Add(new StartupEntry
                {
                    Name = name, Command = command, Source = source, Publisher = publisher, Description = description,
                    ApprovedHive = hive, ApprovedSubKey = approvedKey, ValueName = name,
                    Enabled = ReadEnabled(hive, approvedKey, name),
                });
            }
        }
        catch
        {
            // sem acesso a esta chave
        }
    }

    private static void AddFolder(List<StartupEntry> list, string folder, RegistryHive hive, string source)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(name), Command = file, Source = source,
                    ApprovedHive = hive, ApprovedSubKey = "StartupFolder", ValueName = name,
                    Enabled = ReadEnabled(hive, "StartupFolder", name),
                });
            }
        }
        catch
        {
            // pasta ilegível
        }
    }

    private static bool ReadEnabled(RegistryHive hive, string approvedKey, string valueName)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(ApprovedBase + approvedKey);
            // sem registro = habilitado; o primeiro byte par = habilitado, ímpar (03) = desabilitado
            return key?.GetValue(valueName) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Liga ou desliga. Retorna false se não foi possível gravar (ex.: sem administrador).</summary>
    public static bool SetEnabled(StartupEntry entry, bool enabled)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(entry.ApprovedHive, RegistryView.Registry64);
            using var key = root.CreateSubKey(ApprovedBase + entry.ApprovedSubKey, true);
            var data = new byte[12];
            if (enabled)
            {
                data[0] = 2;
            }
            else
            {
                data[0] = 3;
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            }
            key.SetValue(entry.ValueName, data, RegistryValueKind.Binary);
            entry.Enabled = enabled;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Extrai o executável de uma linha de comando e lê empresa e descrição dele.</summary>
    private static (string Publisher, string Description) Describe(string command)
    {
        try
        {
            string exe = command.Trim();
            if (exe.StartsWith('"'))
            {
                int end = exe.IndexOf('"', 1);
                exe = end > 0 ? exe[1..end] : exe.Trim('"');
            }
            else
            {
                int i = exe.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i > 0) exe = exe[..(i + 4)];
            }
            exe = Environment.ExpandEnvironmentVariables(exe);
            if (!File.Exists(exe)) return ("", "");
            var info = FileVersionInfo.GetVersionInfo(exe);
            return (info.CompanyName?.Trim() ?? "", info.FileDescription?.Trim() ?? "");
        }
        catch
        {
            return ("", "");
        }
    }
}


