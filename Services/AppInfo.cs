namespace Otimizador.App;

/// <summary>Versão do app e de onde ele busca atualizações.</summary>
public static class AppInfo
{
    public const string Name = "Otimizador";
    public const string Author = "Juan";
    public const string AuthorGitHub = "Juan3403";
    public const string AuthorUrl = "https://github.com/Juan3403";

    /// <summary>Repositório oficial do projeto (usado se nada mais estiver configurado).</summary>
    public const string DefaultRepo = "Juan3403/Otimizador";

    public static Version Version { get; } = typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    /// <summary>
    /// Repositório do GitHub (formato "usuario/nome") de onde vêm as atualizações.
    /// Ordem: o que o usuário digitou em Ajustes, depois o arquivo "update-repo.txt" ao lado do programa
    /// (gravado pelo script de release). Sem nenhum dos dois vale o repositório oficial do projeto (DefaultRepo).
    /// </summary>
    public static string Repo
    {
        get
        {
            var fromSettings = Settings.Load().UpdateRepo?.Trim();
            if (IsValidRepo(fromSettings)) return fromSettings!;
            try
            {
                var file = Path.Combine(AppContext.BaseDirectory, "update-repo.txt");
                if (File.Exists(file))
                {
                    var line = File.ReadLines(file).FirstOrDefault()?.Trim();
                    if (IsValidRepo(line)) return line!;
                }
            }
            catch
            {
                // sem o arquivo
            }
            return DefaultRepo;
        }
    }

    public static bool IsValidRepo(string? repo) =>
        !string.IsNullOrWhiteSpace(repo)
        && System.Text.RegularExpressions.Regex.IsMatch(repo.Trim(), @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$");

    public static string RepoUrl => Repo.Length == 0 ? "https://github.com" : $"https://github.com/{Repo}";
}

