using Microsoft.Win32;

namespace Otimizador.App.Tweaks;

public static class TweakCatalog
{
    private const RegistryHive User = RegistryHive.CurrentUser;
    private const RegistryHive Machine = RegistryHive.LocalMachine;

    private static readonly string[] Safe = { "safe", "balanced", "gaming" };
    private static readonly string[] Balanced = { "balanced", "gaming" };
    private static readonly string[] Gaming = { "gaming" };

    public static IReadOnlyList<Tweak> All { get; } = new Tweak[]
    {
        new RegistryTweak
        {
            Id = "adid", Profiles = Safe,
            NamePt = "Desativar ID de publicidade", NameEn = "Disable advertising ID",
            DescPt = "Impede que apps usem um identificador para anúncios personalizados.",
            DescEn = "Stops apps from using an identifier for personalized ads.",
            Entries = new[] { new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0) },
        },
        new RegistryTweak
        {
            Id = "suggestions", Profiles = Safe,
            NamePt = "Desativar sugestões e dicas do Windows", NameEn = "Disable Windows suggestions and tips",
            DescPt = "Remove propagandas e sugestões no Menu Iniciar e nas configurações.",
            DescEn = "Removes ads and suggestions in the Start menu and Settings.",
            Entries = new[]
            {
                new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0),
                new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0),
                new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-310093Enabled", 0),
                new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0),
            },
        },
        new RegistryTweak
        {
            Id = "bing", Profiles = Safe,
            NamePt = "Pesquisa do Menu Iniciar só no PC", NameEn = "Start menu search on this PC only",
            DescPt = "Desliga resultados da web (Bing) na pesquisa do Menu Iniciar.",
            DescEn = "Turns off web (Bing) results in Start menu search.",
            Entries = new[] { new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0) },
        },
        new RegistryTweak
        {
            Id = "startupdelay", Profiles = Balanced,
            NamePt = "Remover atraso dos apps de inicialização", NameEn = "Remove startup apps delay",
            DescPt = "Os programas de inicialização abrem sem esperar o Windows terminar de carregar.",
            DescEn = "Startup programs open without waiting for Windows to finish loading.",
            Entries = new[] { new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0) },
        },
        new RegistryTweak
        {
            Id = "telemetry", Profiles = Balanced, NeedsAdmin = true,
            NamePt = "Reduzir telemetria", NameEn = "Reduce telemetry",
            DescPt = "Reduz a coleta de dados de diagnóstico ao mínimo permitido pela sua edição do Windows.",
            DescEn = "Reduces diagnostic data collection to the minimum your Windows edition allows.",
            Entries = new[] { new RegEntry(Machine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0) },
        },
        new RegistryTweak
        {
            Id = "gamemode", Profiles = Balanced,
            NamePt = "Ativar Modo de Jogo", NameEn = "Enable Game Mode",
            DescPt = "O Windows prioriza o jogo em primeiro plano.",
            DescEn = "Windows prioritizes the game in the foreground.",
            Entries = new[]
            {
                new RegEntry(User, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
                new RegEntry(User, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1),
            },
        },
        new RegistryTweak
        {
            Id = "gamedvr", Profiles = Balanced,
            NamePt = "Desativar gravação em segundo plano (Game DVR)", NameEn = "Disable background recording (Game DVR)",
            DescPt = "Evita o custo de desempenho da gravação automática de jogos.",
            DescEn = "Avoids the performance cost of automatic game recording.",
            Entries = new[]
            {
                new RegEntry(User, @"System\GameConfigStore", "GameDVR_Enabled", 0),
                new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
            },
        },
        new RegistryTweak
        {
            Id = "hags", Profiles = Gaming, NeedsAdmin = true, NeedsReboot = true, Risk = Risk.Medium,
            NamePt = "Agendamento de GPU acelerado por hardware", NameEn = "Hardware-accelerated GPU scheduling",
            DescPt = "Pode reduzir a latência em jogos. Em alguns PCs causa problemas: desfaça se notar instabilidade.",
            DescEn = "May reduce latency in games. On some PCs it causes issues: undo it if you notice instability.",
            Entries = new[] { new RegEntry(Machine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2) },
        },
        new RegistryTweak
        {
            Id = "menudelay", Profiles = Balanced, NeedsReboot = true,
            NamePt = "Menus abrem sem atraso", NameEn = "Menus open without delay",
            DescPt = "Remove o atraso de 0,4 s para abrir menus. Pode exigir sair e entrar no Windows.",
            DescEn = "Removes the 0.4 s delay before menus open. May require signing out and in.",
            Entries = new[] { new RegEntry(User, @"Control Panel\Desktop", "MenuShowDelay", 0, "0") },
        },
        new RegistryTweak
        {
            Id = "transparency", Profiles = Gaming,
            NamePt = "Desligar transparência do Windows", NameEn = "Turn off Windows transparency",
            DescPt = "Menos efeitos visuais: libera um pouco da GPU e deixa a interface mais nítida.",
            DescEn = "Fewer visual effects: frees a little GPU and makes the interface crisper.",
            Entries = new[] { new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0) },
        },
        new RegistryTweak
        {
            Id = "animations", Profiles = Gaming, NeedsReboot = true,
            NamePt = "Desligar animações de janelas", NameEn = "Turn off window animations",
            DescPt = "Janelas e barra de tarefas respondem na hora, sem animação. Pode exigir sair e entrar no Windows.",
            DescEn = "Windows and the taskbar respond instantly, without animation. May require signing out and in.",
            Entries = new[]
            {
                new RegEntry(User, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", 0, "0"),
                new RegEntry(User, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0),
            },
        },
        new RegistryTweak
        {
            Id = "mouseaccel", Profiles = Gaming, NeedsReboot = true,
            NamePt = "Desativar aceleração do mouse", NameEn = "Disable mouse acceleration",
            DescPt = "O cursor se move na mesma proporção da mão, mais previsível em jogos de mira. Pode exigir sair e entrar.",
            DescEn = "The cursor moves in proportion to your hand, more predictable in aiming games. May require signing out and in.",
            Entries = new[]
            {
                new RegEntry(User, @"Control Panel\Mouse", "MouseSpeed", 0, "0"),
                new RegEntry(User, @"Control Panel\Mouse", "MouseThreshold1", 0, "0"),
                new RegEntry(User, @"Control Panel\Mouse", "MouseThreshold2", 0, "0"),
            },
        },
        new RegistryTweak
        {
            Id = "diagtrack", NeedsAdmin = true, NeedsReboot = true,
            NamePt = "Desligar serviço de telemetria (DiagTrack)", NameEn = "Turn off telemetry service (DiagTrack)",
            DescPt = "O serviço que envia dados de diagnóstico à Microsoft deixa de iniciar com o Windows.",
            DescEn = "The service that sends diagnostic data to Microsoft stops starting with Windows.",
            Entries = new[] { new RegEntry(Machine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4) },
        },
        new RegistryTweak
        {
            Id = "sysmain", NeedsAdmin = true, NeedsReboot = true, Risk = Risk.Medium,
            NamePt = "Desligar SysMain (Superfetch)", NameEn = "Turn off SysMain (Superfetch)",
            DescPt = "Recomendado só em PCs com SSD: reduz uso de disco e memória em segundo plano. Em HD pode deixar mais lento.",
            DescEn = "Recommended only on SSD PCs: reduces background disk and memory use. On an HDD it may slow things down.",
            Entries = new[] { new RegEntry(Machine, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4) },
        },
        new HibernateTweak
        {
            Id = "hibernate", NeedsAdmin = true, Risk = Risk.Medium,
            NamePt = "Desligar hibernação (libera o hiberfil.sys)", NameEn = "Turn off hibernation (frees hiberfil.sys)",
            DescPt = "Libera vários GB de disco (o arquivo hiberfil.sys). Desliga também a inicialização rápida e a hibernação.",
            DescEn = "Frees several GB of disk (the hiberfil.sys file). Also turns off fast startup and hibernation.",
        },
        new DnsTweak
        {
            Id = "dns", NeedsAdmin = true, Risk = Risk.Medium,
            NamePt = "DNS rápido (Cloudflare 1.1.1.1)", NameEn = "Fast DNS (Cloudflare 1.1.1.1)",
            DescPt = "Troca o DNS dos adaptadores ativos. Costuma abrir sites mais rápido. Desfazer volta ao DNS de antes.",
            DescEn = "Switches the DNS of active adapters. Usually opens sites faster. Undo goes back to the previous DNS.",
        },        new PowerPlanTweak
        {
            Id = "powerplan", Profiles = Gaming, Risk = Risk.Medium,
            NamePt = "Plano de energia: Alto desempenho", NameEn = "Power plan: High performance",
            DescPt = "A CPU não reduz o clock por economia. Aumenta consumo e calor.",
            DescEn = "The CPU does not downclock to save power. Increases consumption and heat.",
        },
    };
}

