using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace Otimizador.App.Tweaks;

public enum Risk { Low, Medium }

public enum TweakOutcome { Applied, Reverted, SkippedNeedsAdmin, NothingToUndo, Error }

public sealed record TweakResult(Tweak Tweak, TweakOutcome Outcome, string? Detail = null);

/// <summary>Valores originais de cada ajuste (null = o valor não existia), salvos para poder desfazer.</summary>
public sealed class TweakStateStore
{
    private static string FilePath => Path.Combine(AppServices.DataDir, "tweaks-state.json");

    private Dictionary<string, Dictionary<string, string?>> _data = new();

    public static TweakStateStore Load()
    {
        var store = new TweakStateStore();
        try
        {
            store._data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(
                File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            // sem estado salvo ainda
        }
        return store;
    }

    public Dictionary<string, string?>? Get(string tweakId) => _data.GetValueOrDefault(tweakId);

    public void Set(string tweakId, Dictionary<string, string?> values)
    {
        _data[tweakId] = values;
        Save();
    }

    public void Remove(string tweakId)
    {
        if (_data.Remove(tweakId)) Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(AppServices.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public abstract class Tweak
{
    public required string Id { get; init; }
    public required string NamePt { get; init; }
    public required string NameEn { get; init; }
    public required string DescPt { get; init; }
    public required string DescEn { get; init; }
    public Risk Risk { get; init; } = Risk.Low;
    public bool NeedsAdmin { get; init; }
    public bool NeedsReboot { get; init; }

    /// <summary>Perfis que incluem este ajuste: safe, balanced, gaming.</summary>
    public string[] Profiles { get; init; } = Array.Empty<string>();

    public string Name => Loc.T(NamePt, NameEn);
    public string Description => Loc.T(DescPt, DescEn);

    public abstract bool IsApplied();

    /// <summary>Aplica o ajuste; guarda o valor original em <paramref name="store"/> na primeira vez.</summary>
    public abstract void Apply(TweakStateStore store);

    /// <summary>Volta ao valor original salvo. Retorna false se não havia nada salvo.</summary>
    public abstract bool Revert(TweakStateStore store);

    /// <summary>Descreve o que será alterado (pré-visualização).</summary>
    public abstract string Preview();
}

public sealed record RegEntry(RegistryHive Hive, string Path, string Name, int Value, string? Text = null)
{
    public string Key => $"{Hive}\\{Path}\\{Name}";

    /// <summary>Valor esperado como texto (número para DWORD, ou o texto para valores do tipo string).</summary>
    public string Expected => Text ?? Value.ToString();

    public string Display => $"{(Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}\\{Path}\\{Name} = {(Text is null ? Value.ToString() : $"\"{Text}\"")}";
}

public sealed class RegistryTweak : Tweak
{
    public required RegEntry[] Entries { get; init; }

    private static RegistryKey Base(RegistryHive h) => RegistryKey.OpenBaseKey(h, RegistryView.Registry64);

    /// <summary>Valor atual como texto (null se não existe).</summary>
    private static string? ReadValue(RegEntry e)
    {
        using var root = Base(e.Hive);
        using var key = root.OpenSubKey(e.Path);
        return key?.GetValue(e.Name) switch
        {
            int i => i.ToString(),
            string str => str,
            _ => null,
        };
    }

    public override bool IsApplied() => Entries.All(e => ReadValue(e) == e.Expected);

    public override void Apply(TweakStateStore store)
    {
        if (store.Get(Id) is null)
            store.Set(Id, Entries.ToDictionary(e => e.Key, e => ReadValue(e)));

        foreach (var e in Entries)
        {
            using var root = Base(e.Hive);
            using var key = root.CreateSubKey(e.Path, true);
            if (e.Text is null) key.SetValue(e.Name, e.Value, RegistryValueKind.DWord);
            else key.SetValue(e.Name, e.Text, RegistryValueKind.String);
        }
    }

    public override bool Revert(TweakStateStore store)
    {
        var saved = store.Get(Id);
        if (saved is null) return false;

        foreach (var e in Entries)
        {
            if (!saved.TryGetValue(e.Key, out var original)) continue;
            using var root = Base(e.Hive);
            if (original is null)
            {
                using var key = root.OpenSubKey(e.Path, true);
                key?.DeleteValue(e.Name, false);
            }
            else
            {
                using var key = root.CreateSubKey(e.Path, true);
                if (e.Text is null) key.SetValue(e.Name, int.Parse(original), RegistryValueKind.DWord);
                else key.SetValue(e.Name, original, RegistryValueKind.String);
            }
        }
        store.Remove(Id);
        return true;
    }

    public override string Preview() => string.Join("; ", Entries.Select(e => e.Display));
}

public sealed class PowerPlanTweak : Tweak
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string AppliedKey = "applied";
    private const string PreviousKey = "previous";

    private static string? Run(string args)
    {
        var psi = new ProcessStartInfo("powercfg", args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 ? output : null;
    }

    private static string? ParseGuid(string? text)
    {
        if (text is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text, "[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private static string? ActiveScheme() => ParseGuid(Run("/getactivescheme"));

    public override bool IsApplied()
    {
        var active = ActiveScheme();
        if (active is null) return false;
        if (active == HighPerformance) return true;
        var saved = TweakStateStore.Load().Get(Id);
        return saved is not null && saved.GetValueOrDefault(AppliedKey) == active;
    }

    public override void Apply(TweakStateStore store)
    {
        var previous = ActiveScheme() ?? throw new InvalidOperationException("powercfg");
        var target = HighPerformance;
        if (Run($"/setactive {target}") is null)
        {
            // Planos modernos (Modern Standby) podem não ter Alto desempenho: cria uma cópia.
            target = ParseGuid(Run($"/duplicatescheme {HighPerformance}")) ?? throw new InvalidOperationException("powercfg");
            if (Run($"/setactive {target}") is null) throw new InvalidOperationException("powercfg");
        }
        if (store.Get(Id) is null)
            store.Set(Id, new Dictionary<string, string?> { [PreviousKey] = previous, [AppliedKey] = target });
    }

    public override bool Revert(TweakStateStore store)
    {
        var saved = store.Get(Id);
        if (saved?.GetValueOrDefault(PreviousKey) is not { } previous) return false;
        if (Run($"/setactive {previous}") is null) throw new InvalidOperationException("powercfg");
        store.Remove(Id);
        return true;
    }

    public override string Preview() => Loc.T("Plano de energia ativo → Alto desempenho", "Active power plan → High performance");
}

