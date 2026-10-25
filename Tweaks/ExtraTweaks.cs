using System.Diagnostics;
using Microsoft.Win32;

namespace Otimizador.App.Tweaks;

/// <summary>Hibernação desligada: apaga o hiberfil.sys (vários GB) e desliga a inicialização rápida.</summary>
public sealed class HibernateTweak : Tweak
{
    private const string MarkerKey = "wasOn";

    private static bool Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(30_000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public override bool IsApplied()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
        return key?.GetValue("HibernateEnabled") is int i && i == 0;
    }

    public override void Apply(TweakStateStore store)
    {
        if (!Run("/h off")) throw new InvalidOperationException("powercfg /h off");
        if (store.Get(Id) is null) store.Set(Id, new Dictionary<string, string?> { [MarkerKey] = "1" });
    }

    public override bool Revert(TweakStateStore store)
    {
        if (store.Get(Id) is null) return false;
        if (!Run("/h on")) throw new InvalidOperationException("powercfg /h on");
        store.Remove(Id);
        return true;
    }

    public override string Preview() => Loc.T("powercfg /h off (apaga o hiberfil.sys e desliga a inicialização rápida)", "powercfg /h off (deletes hiberfil.sys and turns off fast startup)");
}

/// <summary>DNS rápido e privado (Cloudflare 1.1.1.1) nos adaptadores de rede ativos; guarda os DNS anteriores para voltar.</summary>
public sealed class DnsTweak : Tweak
{
    private const string Primary = "1.1.1.1";

    private static List<(string Index, string Servers)> Adapters()
    {
        var output = TweakExecutor.RunPowerShell(
            "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { $i = $_.ifIndex; " +
            "$d = (Get-DnsClientServerAddress -InterfaceIndex $i -AddressFamily IPv4 -ErrorAction SilentlyContinue).ServerAddresses -join ','; \"$i|$d\" }", 60_000);
        var list = new List<(string, string)>();
        foreach (var line in (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p.Length == 2 && int.TryParse(p[0], out _)) list.Add((p[0], p[1]));
        }
        return list;
    }

    public override bool IsApplied()
    {
        var a = Adapters();
        return a.Count > 0 && a.All(x => x.Servers.StartsWith(Primary, StringComparison.Ordinal));
    }

    public override void Apply(TweakStateStore store)
    {
        var adapters = Adapters();
        if (adapters.Count == 0) throw new InvalidOperationException("sem adaptador de rede ativo");
        if (store.Get(Id) is null)
            store.Set(Id, adapters.ToDictionary(a => a.Index, a => (string?)a.Servers));
        foreach (var (index, _) in adapters)
            TweakExecutor.RunPowerShell($"Set-DnsClientServerAddress -InterfaceIndex {index} -ServerAddresses 1.1.1.1,1.0.0.1 -ErrorAction Stop", 30_000);
    }

    public override bool Revert(TweakStateStore store)
    {
        var saved = store.Get(Id);
        if (saved is null) return false;
        foreach (var (index, servers) in saved)
        {
            if (!int.TryParse(index, out _)) continue;
            var cmd = string.IsNullOrEmpty(servers)
                ? $"Set-DnsClientServerAddress -InterfaceIndex {index} -ResetServerAddresses -ErrorAction SilentlyContinue"
                : $"Set-DnsClientServerAddress -InterfaceIndex {index} -ServerAddresses {servers} -ErrorAction SilentlyContinue";
            TweakExecutor.RunPowerShell(cmd, 30_000);
        }
        store.Remove(Id);
        return true;
    }

    public override string Preview() => Loc.T("DNS dos adaptadores ativos → 1.1.1.1 e 1.0.0.1 (Cloudflare)", "DNS of active adapters → 1.1.1.1 and 1.0.0.1 (Cloudflare)");
}
