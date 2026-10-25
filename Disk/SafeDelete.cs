using Microsoft.VisualBasic.FileIO;

namespace Otimizador.App.Disk;

/// <summary>Exclusão segura: só envia para a Lixeira (dá para restaurar) e recusa pastas e arquivos do sistema.</summary>
public static class SafeDelete
{
    private static readonly string[] ProtectedRoots = BuildProtectedRoots();

    private static string[] BuildProtectedRoots()
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p)) list.Add(p.TrimEnd('\\'));
        }
        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
        Add(drive + @"\System Volume Information");
        Add(drive + @"\$Recycle.Bin");
        Add(drive + @"\Recovery");
        Add(drive + @"\Boot");
        return list.ToArray();
    }

    private static readonly string[] ProtectedFiles = { "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "BOOTNXT" };

    /// <summary>True se o caminho é do sistema, de programas instalados, a raiz de um disco ou a pasta pessoal inteira.</summary>
    public static bool IsProtected(string path)
    {
        var p = path.TrimEnd('\\');
        if (p.Length <= 2) return true; // "C:" = raiz do disco
        if (Path.GetPathRoot(path)?.TrimEnd('\\').Equals(p, StringComparison.OrdinalIgnoreCase) == true) return true;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        if (p.Equals(profile, StringComparison.OrdinalIgnoreCase)) return true;
        if (Path.GetDirectoryName(p)?.Equals(Path.GetDirectoryName(profile), StringComparison.OrdinalIgnoreCase) == true
            && Path.GetFileName(p).Equals("AppData", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var root in ProtectedRoots)
        {
            if (p.Equals(root, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }

        var name = Path.GetFileName(p);
        return ProtectedFiles.Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Envia para a Lixeira. Retorna null se deu certo ou a mensagem de erro.</summary>
    public static string? ToRecycleBin(string path, bool isDirectory)
    {
        if (IsProtected(path)) return "protegido";
        try
        {
            if (isDirectory)
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
