using System.Text.Json;

namespace Otimizador.App;

/// <summary>Preferências do usuário (tema, idioma e overlay), salvas na pasta de dados.</summary>
public sealed class Settings
{
    public string? Theme { get; set; }
    public string? Language { get; set; }

    public double? OverlayX { get; set; }
    public double? OverlayY { get; set; }
    public int OverlayScale { get; set; } = 100;
    public bool OverlayLocked { get; set; }
    public bool OverlayCpu { get; set; } = true;
    public bool OverlayGpu { get; set; } = true;
    public bool OverlayRam { get; set; } = true;
    public bool OverlayDisk { get; set; }

    // Atualização
    public string? UpdateRepo { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }

    // Bandeja e inicialização
    public bool TrayEnabled { get; set; } = true;
    public bool CloseToTray { get; set; }
    public bool StartMinimized { get; set; }

    // Modo jogo (desligado por padrão)
    public bool GameModeEnabled { get; set; }
    public bool GameModeOverlay { get; set; } = true;

    // Limpeza agendada (desligada por padrão)
    public bool ScheduledCleanup { get; set; }
    public int CleanupWeekday { get; set; } = (int)DayOfWeek.Sunday;
    public int CleanupHour { get; set; } = 12;
    public DateTime? LastScheduledCleanup { get; set; }

    // Inventário da empresa (desligado por padrão)
    public bool InventoryEnabled { get; set; }
    public string? InventoryFolder { get; set; }

    public bool AlertsEnabled { get; set; } = true;
    public int AlertCpu { get; set; } = 90;
    public int AlertGpu { get; set; } = 85;
    public int AlertDiskFree { get; set; } = 10;
    public int AlertRam { get; set; } = 95;

    private static string FilePath => Path.Combine(AppServices.DataDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppServices.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // preferências são opcionais
        }
    }
}


