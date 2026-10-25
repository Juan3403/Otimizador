using System.Text.Json;

namespace Otimizador.App.Security;

public enum SecLevel { Good = 0, Warning = 1, Bad = 2, Unknown = 3 }

public sealed record SecurityItem(string Key, string Title, SecLevel Level, string Detail);

/// <summary>Verificação básica de segurança do Windows: antivírus, firewall, criptografia, inicialização segura e atualizações.</summary>
public static class SecurityService
{
    /// <summary>Resultado da última verificação (usado pelo inventário e pelo relatório).</summary>
    public static List<SecurityItem>? Last { get; private set; }

    private const string Script = """
        $ErrorActionPreference = 'SilentlyContinue'
        $r = [ordered]@{}
        $d = Get-MpComputerStatus
        if ($d) { $r.defender = @{ enabled = [bool]$d.AntivirusEnabled; realtime = [bool]$d.RealTimeProtectionEnabled; sigAge = [int]$d.AntivirusSignatureAge } } else { $r.defender = $null }
        $r.av = @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | ForEach-Object { @{ name = [string]$_.displayName; on = [bool](($_.productState -band 0x1000) -ne 0) } })
        $r.firewall = @(Get-NetFirewallProfile | ForEach-Object { @{ name = [string]$_.Name; on = ([string]$_.Enabled -eq 'True') } })
        $b = Get-BitLockerVolume -MountPoint $env:SystemDrive
        if ($b) { $r.bitlocker = @{ on = ([string]$b.ProtectionStatus -eq 'On' -or [string]$b.ProtectionStatus -eq '1') } } else { $r.bitlocker = $null }
        $sb = Confirm-SecureBootUEFI
        $r.secureboot = $sb
        $t = Get-Tpm
        if ($t) { $r.tpm = @{ present = [bool]$t.TpmPresent; ready = [bool]$t.TpmReady } } else { $r.tpm = $null }
        $r.uac = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System').EnableLUA
        $r.reboot = [bool]((Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'))
        $r | ConvertTo-Json -Depth 5 -Compress
        """;

    private const string UpdatesScript = """
        $ErrorActionPreference = 'Stop'
        $s = New-Object -ComObject Microsoft.Update.Session
        $res = $s.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0 and Type='Software'")
        @{ count = [int]$res.Updates.Count; titles = @($res.Updates | Select-Object -First 5 | ForEach-Object { [string]$_.Title }) } | ConvertTo-Json -Compress
        """;

    private static IEnumerable<JsonElement> AsArray(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? new[] { e } : Enumerable.Empty<JsonElement>();

    private static bool? Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    /// <summary>Verificações rápidas (alguns segundos). A busca de atualizações do Windows é separada: CheckUpdatesAsync.</summary>
    public static List<SecurityItem> Check()
    {
        var items = new List<SecurityItem>();
        var json = PsRunner.Run(Script, 60_000);
        if (string.IsNullOrWhiteSpace(json))
        {
            items.Add(new SecurityItem("error", Loc.T("Verificação de segurança", "Security check"), SecLevel.Unknown,
                Loc.T("Não foi possível executar a verificação.", "Could not run the check.")));
            return items;
        }
        items.AddRange(Parse(json));
        if (!AppServices.IsAdmin)
        {
            // BitLocker, Secure Boot e TPM só podem ser lidos como administrador: não acusar problema que pode nem existir
            for (int i = 0; i < items.Count; i++)
                if (items[i].Key is "bitlocker" or "secureboot" or "tpm")
                    items[i] = items[i] with { Level = SecLevel.Unknown, Detail = Loc.T("Abra o Otimizador como administrador para ver", "Open Optimizer as administrator to see this") };
        }
        Last = items;
        return items;
    }

    /// <summary>Interpreta o JSON do script (separado para poder testar sem executar o PowerShell).</summary>
    public static List<SecurityItem> Parse(string json)
    {
        var items = new List<SecurityItem>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Antivírus
        bool defenderOn = false;
        int? sigAge = null;
        if (root.TryGetProperty("defender", out var def) && def.ValueKind == JsonValueKind.Object)
        {
            defenderOn = def.TryGetProperty("realtime", out var rt) && rt.ValueKind == JsonValueKind.True;
            if (def.TryGetProperty("sigAge", out var sa) && sa.ValueKind == JsonValueKind.Number) sigAge = sa.GetInt32();
        }
        var thirdParty = root.TryGetProperty("av", out var av)
            ? AsArray(av).Where(a => a.TryGetProperty("on", out var o) && o.ValueKind == JsonValueKind.True)
                .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                .Where(n => !string.IsNullOrEmpty(n) && !n.Contains("Defender", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string>();
        string avTitle = Loc.T("Antivírus", "Antivirus");
        if (defenderOn) items.Add(new SecurityItem("av", avTitle, SecLevel.Good, Loc.T("Microsoft Defender ativo (proteção em tempo real)", "Microsoft Defender active (real-time protection)")));
        else if (thirdParty.Count > 0) items.Add(new SecurityItem("av", avTitle, SecLevel.Good, string.Join(", ", thirdParty) + Loc.T(" ativo", " active")));
        else items.Add(new SecurityItem("av", avTitle, SecLevel.Bad, Loc.T("Nenhum antivírus ativo. Ligue o Microsoft Defender em Segurança do Windows.", "No active antivirus. Turn on Microsoft Defender in Windows Security.")));

        if (defenderOn && sigAge is { } age)
            items.Add(new SecurityItem("sig", Loc.T("Definições de vírus", "Virus definitions"),
                age <= 3 ? SecLevel.Good : age <= 14 ? SecLevel.Warning : SecLevel.Bad,
                Loc.T($"Atualizadas há {age} dia(s)", $"Updated {age} day(s) ago")));

        // Firewall
        if (root.TryGetProperty("firewall", out var fw))
        {
            var profiles = AsArray(fw).ToList();
            if (profiles.Count > 0)
            {
                int on = profiles.Count(p => p.TryGetProperty("on", out var o) && o.ValueKind == JsonValueKind.True);
                items.Add(new SecurityItem("fw", Loc.T("Firewall", "Firewall"),
                    on == profiles.Count ? SecLevel.Good : on == 0 ? SecLevel.Bad : SecLevel.Warning,
                    Loc.T($"{on} de {profiles.Count} perfis ligados", $"{on} of {profiles.Count} profiles on")));
            }
        }

        // Criptografia do disco
        string bitTitle = Loc.T("Criptografia do disco (BitLocker)", "Disk encryption (BitLocker)");
        if (root.TryGetProperty("bitlocker", out var bl) && bl.ValueKind == JsonValueKind.Object)
        {
            bool on = bl.TryGetProperty("on", out var o) && o.ValueKind == JsonValueKind.True;
            items.Add(new SecurityItem("bitlocker", bitTitle, on ? SecLevel.Good : SecLevel.Warning,
                on ? Loc.T("Disco do sistema criptografado", "System drive encrypted")
                   : Loc.T("Desligada: se o computador for roubado, os dados ficam acessíveis", "Off: if the computer is stolen the data is accessible")));
        }
        else items.Add(new SecurityItem("bitlocker", bitTitle, SecLevel.Unknown, Loc.T("Indisponível nesta edição do Windows", "Not available in this Windows edition")));

        // Inicialização segura / TPM
        var sb = Bool(root, "secureboot");
        items.Add(new SecurityItem("secureboot", Loc.T("Inicialização segura (Secure Boot)", "Secure Boot"),
            sb is true ? SecLevel.Good : sb is false ? SecLevel.Warning : SecLevel.Unknown,
            sb is true ? Loc.T("Ligada", "On") : sb is false ? Loc.T("Desligada na BIOS", "Off in BIOS") : Loc.T("Sem informação (BIOS antiga ou sem UEFI)", "No information (legacy BIOS or no UEFI)")));

        if (root.TryGetProperty("tpm", out var tpm) && tpm.ValueKind == JsonValueKind.Object)
        {
            bool present = tpm.TryGetProperty("present", out var p) && p.ValueKind == JsonValueKind.True;
            bool ready = tpm.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True;
            items.Add(new SecurityItem("tpm", "TPM", present && ready ? SecLevel.Good : SecLevel.Warning,
                present ? (ready ? Loc.T("Presente e pronto", "Present and ready") : Loc.T("Presente, mas não pronto", "Present but not ready")) : Loc.T("Não encontrado", "Not found")));
        }

        // UAC
        if (root.TryGetProperty("uac", out var uac) && uac.ValueKind == JsonValueKind.Number)
            items.Add(new SecurityItem("uac", Loc.T("Controle de Conta de Usuário (UAC)", "User Account Control (UAC)"),
                uac.GetInt32() == 1 ? SecLevel.Good : SecLevel.Bad,
                uac.GetInt32() == 1 ? Loc.T("Ligado", "On") : Loc.T("Desligado: programas podem mudar o sistema sem avisar", "Off: programs can change the system without asking")));

        // Reinício pendente
        if (Bool(root, "reboot") is { } reboot)
            items.Add(new SecurityItem("reboot", Loc.T("Reinício pendente", "Pending restart"),
                reboot ? SecLevel.Warning : SecLevel.Good,
                reboot ? Loc.T("O Windows precisa reiniciar para concluir atualizações", "Windows needs to restart to finish updates") : Loc.T("Nenhum", "None")));

        return items;
    }

    public static SecurityItem ParseUpdates(string? json)
    {
        string title = Loc.T("Atualizações do Windows", "Windows updates");
        if (string.IsNullOrWhiteSpace(json))
            return new SecurityItem("updates", title, SecLevel.Unknown, Loc.T("Não foi possível verificar", "Could not check"));

        using var doc = JsonDocument.Parse(json);
        int count = doc.RootElement.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
        if (count == 0) return new SecurityItem("updates", title, SecLevel.Good, Loc.T("Tudo em dia", "Up to date"));

        string names = "";
        if (doc.RootElement.TryGetProperty("titles", out var titles))
        {
            var list = titles.ValueKind == JsonValueKind.Array
                ? titles.EnumerateArray().Select(x => x.GetString())
                : new[] { titles.GetString() };
            names = string.Join("; ", list.Where(x => !string.IsNullOrEmpty(x)).Take(3));
        }
        return new SecurityItem("updates", title, count >= 10 ? SecLevel.Bad : SecLevel.Warning,
            Loc.T($"{count} atualização(ões) pendente(s)", $"{count} pending update(s)") + (names.Length > 0 ? ": " + names : ""));
    }
    /// <summary>Busca atualizações do Windows pendentes (leva de 10 a 60 segundos).</summary>
    public static Task<SecurityItem> CheckUpdatesAsync() =>
        Task.Run(() => ParseUpdates(PsRunner.Run(UpdatesScript, 120_000)));

    public static (int Good, int Total) Score(IEnumerable<SecurityItem> items)
    {
        var known = items.Where(i => i.Level != SecLevel.Unknown).ToList();
        return (known.Count(i => i.Level == SecLevel.Good), known.Count);
    }
}


