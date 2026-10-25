using System.IO;
using System.Windows;
using Otimizador.App;
using Otimizador.App.Bench;
using Otimizador.App.Cleanup;
using Otimizador.App.Disk;
using Otimizador.App.Tweaks;
using Otimizador.App.Updates;
using Xunit;

namespace Otimizador.Tests;

public class UpdateTests
{
    private const string Release = """
        {"tag_name":"v9.8.7","html_url":"https://github.com/x/y/releases/tag/v9.8.7","body":"Novidades",
         "assets":[
          {"name":"Otimizador-Setup-9.8.7.exe","browser_download_url":"https://github.com/x/y/releases/download/v9.8.7/Otimizador-Setup-9.8.7.exe"},
          {"name":"Otimizador-Portatil-9.8.7.zip","browser_download_url":"https://github.com/x/y/releases/download/v9.8.7/Otimizador-Portatil-9.8.7.zip"},
          {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/x/y/releases/download/v9.8.7/SHA256SUMS.txt"},
          {"name":"malicioso.exe","browser_download_url":"http://evil.example.com/Setup.exe"}]}
        """;

    [Fact]
    public void ParseRelease_reads_version_and_assets()
    {
        var info = UpdateService.ParseRelease(Release);
        Assert.NotNull(info);
        Assert.Equal(new Version(9, 8, 7), info!.Version);
        Assert.NotNull(info.InstallerUrl);
        Assert.NotNull(info.ZipUrl);
        Assert.NotNull(info.ChecksumsUrl);
    }

    [Fact]
    public void ParseRelease_rejects_garbage() => Assert.Null(UpdateService.ParseRelease("isto não é json"));

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("V10.0.1-beta", 10, 0, 1)]
    public void TryParseVersion_accepts_tags(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateService.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    public void TryParseVersion_rejects_non_versions(string tag) => Assert.False(UpdateService.TryParseVersion(tag, out _));

    [Theory]
    [InlineData("https://github.com/x/y/releases/download/v1/a.exe", true)]
    [InlineData("https://objects.githubusercontent.com/a", true)]
    [InlineData("http://github.com/a", false)]
    [InlineData("https://evil.example.com/a", false)]
    [InlineData("https://github.com.evil.com/a", false)]
    [InlineData("não é endereço", false)]
    public void IsAllowedUrl_only_https_github(string url, bool expected) => Assert.Equal(expected, UpdateService.IsAllowedUrl(url));

    [Fact]
    public void FindHash_matches_by_file_name()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        string sums = $"{a}  Setup.exe\n{b} *outro.zip";
        Assert.Equal(a, UpdateService.FindHash(sums, "Setup.exe"));
        Assert.Equal(new string('b', 64), UpdateService.FindHash(sums, "OUTRO.zip"));
        Assert.Null(UpdateService.FindHash(sums, "inexistente.exe"));
    }

    [Theory]
    [InlineData("Juan3403/Otimizador", true)]
    [InlineData("a b/c", false)]
    [InlineData("../x", false)]
    [InlineData("", false)]
    public void IsValidRepo(string repo, bool expected) => Assert.Equal(expected, AppInfo.IsValidRepo(repo));
}

public class SafetyTests
{
    [Fact]
    public void SafeDelete_protects_windows_program_files_and_drive_root()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.True(SafeDelete.IsProtected(win));
        Assert.True(SafeDelete.IsProtected(Path.Combine(win, "System32", "kernel32.dll")));
        Assert.True(SafeDelete.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.True(SafeDelete.IsProtected(Path.GetPathRoot(win)!));
    }

    [Fact]
    public void SafeDelete_allows_user_files()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.False(SafeDelete.IsProtected(Path.Combine(docs, "qualquer.mp4")));
    }
}

public class ScheduleTests
{
    [Fact]
    public void NextDue_same_day_when_hour_not_reached()
    {
        var last = new DateTime(2026, 10, 25, 11, 0, 0); // domingo
        Assert.Equal(new DateTime(2026, 10, 25, 12, 0, 0), ScheduledCleanupService.NextDue(last, (int)DayOfWeek.Sunday, 12));
    }

    [Fact]
    public void NextDue_next_week_when_already_past()
    {
        var last = new DateTime(2026, 10, 25, 13, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 1, 12, 0, 0), ScheduledCleanupService.NextDue(last, (int)DayOfWeek.Sunday, 12));
    }

    [Fact]
    public void NextDue_from_midweek()
    {
        var last = new DateTime(2026, 10, 21, 10, 0, 0); // quarta
        Assert.Equal(new DateTime(2026, 10, 25, 12, 0, 0), ScheduledCleanupService.NextDue(last, (int)DayOfWeek.Sunday, 12));
    }
}

public class TreemapTests
{
    [Fact]
    public void Layout_fills_the_area_without_overflow()
    {
        var nodes = new[] { 600L, 300L, 200L, 100L, 50L, 50L }
            .Select((s, i) => new DiskNode { Name = "n" + i, Size = s }).ToList();
        var output = new List<(DiskNode Node, Rect Rect)>();
        var bounds = new Rect(0, 0, 400, 300);
        TreemapLayout.Layout(nodes, bounds, output);

        Assert.Equal(nodes.Count, output.Count);
        double area = output.Sum(o => o.Rect.Width * o.Rect.Height);
        Assert.InRange(area, bounds.Width * bounds.Height * 0.999, bounds.Width * bounds.Height * 1.001);
        Assert.All(output, o =>
        {
            Assert.True(o.Rect.Left >= -0.01 && o.Rect.Top >= -0.01);
            Assert.True(o.Rect.Right <= bounds.Right + 0.01 && o.Rect.Bottom <= bounds.Bottom + 0.01);
        });
        // área proporcional ao tamanho
        var big = output.First(o => o.Node.Size == 600).Rect;
        Assert.InRange(big.Width * big.Height / (bounds.Width * bounds.Height), 0.45, 0.55);
    }

    [Fact]
    public void Layout_ignores_empty_input()
    {
        var output = new List<(DiskNode Node, Rect Rect)>();
        TreemapLayout.Layout(new List<DiskNode>(), new Rect(0, 0, 100, 100), output);
        Assert.Empty(output);
    }
}

public class CatalogTests
{
    [Fact]
    public void Tweaks_have_unique_ids_and_both_languages()
    {
        var all = TweakCatalog.All;
        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        Assert.All(all, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.NamePt) || string.IsNullOrWhiteSpace(t.NameEn), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.DescPt) || string.IsNullOrWhiteSpace(t.DescEn), t.Id);
            Assert.All(t.Profiles, p => Assert.Contains(p, new[] { "safe", "balanced", "gaming" }));
        });
    }

    [Fact]
    public void Strings_have_both_languages()
    {
        Assert.All(Strings.Table, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Value.Pt), kv.Key + " (PT)");
            Assert.False(string.IsNullOrWhiteSpace(kv.Value.En), kv.Key + " (EN)");
        });
    }

    [Theory]
    [InlineData("Test_PartCpu")]
    [InlineData("Test_PartRam")]
    [InlineData("Test_PartGpu")]
    [InlineData("Test_PartDisk")]
    [InlineData("Test_HintCpuAdmin")]
    public void New_test_strings_exist(string key) => Assert.True(Strings.Table.ContainsKey(key));
}

public class StressTests
{
    [Fact]
    public void Cpu_and_memory_pass_for_a_short_run()
    {
        StressStatus? last = null;
        var outcome = StressTest.Run(TimeSpan.FromSeconds(2), null, new Progress<StressStatus>(s => last = s), CancellationToken.None,
            StressTargets.Cpu | StressTargets.Memory);
        Assert.Equal(StressOutcome.Passed, outcome);
    }

    [Fact]
    public void Disk_passes_for_a_short_run()
    {
        var outcome = StressTest.Run(TimeSpan.FromSeconds(2), null, new Progress<StressStatus>(_ => { }), CancellationToken.None, StressTargets.Disk);
        Assert.Equal(StressOutcome.Passed, outcome);
    }

    [Fact]
    public void Cancelled_run_reports_cancelled()
    {
        using var cts = new CancellationTokenSource(500);
        var outcome = StressTest.Run(TimeSpan.FromSeconds(30), null, new Progress<StressStatus>(_ => { }), cts.Token, StressTargets.Memory);
        Assert.Equal(StressOutcome.Cancelled, outcome);
    }
}
