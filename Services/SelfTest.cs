using System.Text;
using Microsoft.Win32;
using Otimizador.App.Bench;
using Otimizador.App.Boot;
using Otimizador.App.Cleanup;
using Otimizador.App.GameMode;
using Otimizador.App.Inventory;
using Otimizador.App.Network;
using Otimizador.App.Security;
using Otimizador.App.Updates;
using Otimizador.App.Disk;
using Otimizador.App.Hardware;
using Otimizador.App.Tweaks;

namespace Otimizador.App;

/// <summary>Verificação sem interface: <c>Otimizador.exe --selftest relatorio.txt</c>. Retorna 0 se tudo passou.</summary>
public static class SelfTest
{
    /// <summary>Só o teste de estabilidade por peça, em combinações, com 2 s cada (no máximo 10 s no total).</summary>
    public static int RunStress(string reportPath)
    {
        var sb = new StringBuilder();
        int failures = 0;
        var combos = new (string Name, StressTargets T)[]
        {
            ("CPU", StressTargets.Cpu),
            ("RAM", StressTargets.Memory),
            ("GPU", StressTargets.Gpu),
            ("Disco", StressTargets.Disk),
            ("RAM + GPU", StressTargets.Memory | StressTargets.Gpu),
        };
        foreach (var (name, t) in combos)
        {
            try
            {
                var last = new StressStatus(TimeSpan.Zero, 0, 0, null, null);
                var outcome = StressTest.Run(TimeSpan.FromSeconds(2), AppServices.Sensors,
                    new SyncProgress<StressStatus>(s => last = s), CancellationToken.None, t);
                bool ok = outcome == StressOutcome.Passed && last.Iterations > 0;
                if (last.GpuMissing) ok = outcome == StressOutcome.Passed; // sem GPU compatível: ignorado
                if (!ok) failures++;
                sb.AppendLine($"[{(ok ? "OK" : "FAIL")}] {name}: {outcome}, {last.Iterations} passadas, {last.Errors} erros" +
                              (last.GpuMissing ? " (GPU indisponível)" : "") +
                              (last.MaxGpuTemp is { } g ? $", GPU máx {g:0} °C" : ""));
            }
            catch (Exception ex)
            {
                failures++;
                sb.AppendLine($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        return failures;
    }

    public static int Run(string reportPath, int stressSeconds = 6)
    {
        var sb = new StringBuilder();
        int failures = 0;

        void Check(string name, Func<string> body)
        {
            try
            {
                sb.AppendLine($"[OK]   {name}: {body()}");
            }
            catch (Exception ex)
            {
                failures++;
                sb.AppendLine($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        sb.AppendLine($"Admin: {AppServices.IsAdmin}");
        var none = CancellationToken.None;

        Check("Hardware", () =>
        {
            var sections = HardwareInfoService.Collect();
            var empty = sections.Where(s => s.Lines.Count == 0).Select(s => s.Title).ToList();
            if (empty.Count > 0) throw new Exception("seções vazias: " + string.Join(", ", empty));
            return "\n" + HardwareInfoService.ToText(sections);
        });

        Check("Sensores", () =>
        {
            var snap = AppServices.Sensors.Snapshot();
            if (snap.Count == 0) throw new Exception("nenhum sensor lido");
            return $"{snap.Count} leituras; CPU temp={SensorService.CpuTemp(snap)?.ToString("0") ?? "n/d"}; " +
                   $"CPU uso={SensorService.CpuLoad(snap)?.ToString("0") ?? "n/d"}; RAM uso={SensorService.RamLoad(snap)?.ToString("0") ?? "n/d"}";
        });

        Check("CPU 1 núcleo", () =>
        {
            var r = Benchmarks.CpuSingle(TimeSpan.FromSeconds(1), none);
            if (r.Value <= 0) throw new Exception("pontuação zero");
            return $"{r.Value:0} {r.Unit}";
        });

        Check("CPU multi", () =>
        {
            var r = Benchmarks.CpuMulti(TimeSpan.FromSeconds(1), none);
            if (r.Value <= 0) throw new Exception("pontuação zero");
            return $"{r.Value:0} {r.Unit}";
        });

        Check("Memória", () =>
        {
            var r = Benchmarks.Memory(none, 64, 4);
            if (r.Value <= 0) throw new Exception("pontuação zero");
            return $"{r.Value:0.0} {r.Unit}";
        });

        Check("Disco", () =>
        {
            var (w, r) = Benchmarks.Disk(none, 64);
            if (w.Value <= 0 || r.Value <= 0) throw new Exception("pontuação zero");
            return $"escrita {w.Value:0} MB/s, leitura {r.Value:0} MB/s";
        });

        Check("GPU (Direct3D 11)", () =>
        {
            var gpu = GpuBenchmark.Run(TimeSpan.FromSeconds(1.5), none);
            if (gpu is null) return "sem GPU compatível com Direct3D 11 (teste ignorado)";
            if (gpu.Compute.Value <= 0 || gpu.Memory.Value <= 0) throw new Exception("pontuação zero");
            return $"{gpu.Adapter}: {gpu.Compute.Value:0} GFLOPS, {gpu.Memory.Value:0.0} GB/s";
        });
        Check($"Estabilidade ({stressSeconds} s)", () =>
        {
            var last = new StressStatus(TimeSpan.Zero, 0, 0, null, null);
            var outcome = StressTest.Run(TimeSpan.FromSeconds(stressSeconds), AppServices.Sensors, new SyncProgress<StressStatus>(s => last = s), none);
            if (outcome != StressOutcome.Passed) throw new Exception($"resultado {outcome}, erros {last.Errors}");
            return $"{outcome}, {last.Iterations} iterações, {last.Errors} erros";
        });

        Check("Ajuste: aplicar e desfazer (HKCU)", () =>
        {
            var tweak = TweakCatalog.All.First(t => t.Id == "adid");
            var before = tweak.IsApplied();
            var store = TweakStateStore.Load();
            tweak.Apply(store);
            if (!tweak.IsApplied()) throw new Exception("não ficou aplicado");
            if (!tweak.Revert(TweakStateStore.Load())) throw new Exception("sem estado para desfazer");
            if (before != tweak.IsApplied()) throw new Exception("estado não voltou ao original");
            return $"ok (estado original: {(before ? "ativo" : "inativo")})";
        });

        if (AppServices.IsAdmin)
        {
            Check("Admin: temperatura da CPU", () =>
            {
                var snap = AppServices.Sensors.Snapshot();
                var t = SensorService.CpuTemp(snap);
                if (t is null)
                {
                    var raw = snap.Where(r => r.Type == LibreHardwareMonitor.Hardware.SensorType.Temperature)
                        .Select(r => $"{r.HardwareType}/{r.Name}={r.Value}");
                    throw new Exception("sem leitura de temperatura da CPU. Temperaturas brutas: " + string.Join("; ", raw));
                }
                return $"{t:0} °C";
            });

            foreach (var id in new[] { "telemetry", "hags" })
            {
                Check($"Admin: ajuste {id} aplicar e desfazer (HKLM)", () =>
                {
                    var tweak = TweakCatalog.All.First(t => t.Id == id);
                    var before = tweak.IsApplied();
                    tweak.Apply(TweakStateStore.Load());
                    if (!tweak.IsApplied()) throw new Exception("não ficou aplicado");
                    if (!tweak.Revert(TweakStateStore.Load())) throw new Exception("sem estado para desfazer");
                    if (before != tweak.IsApplied()) throw new Exception("estado não voltou ao original");
                    return "ok";
                });
            }

            // Sem a Proteção do Sistema do Windows o ponto não pode existir: o app avisa o usuário (não é falha do app).
            Check("Admin: ponto de restauração", () => TweakExecutor.EnsureRestorePoint() switch
            {
                TweakExecutor.RestoreResult.Created => "criado e confirmado no Windows",
                TweakExecutor.RestoreResult.RecentExists => "já existe um ponto com menos de 24 h (limite do Windows)",
                _ => "indisponível neste PC (Proteção do Sistema desligada): o app avisa o usuário antes de aplicar",
            });
        }
        Check("Espaço: varredura e mapa", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "otimizador-scan-test-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            try
            {
                File.WriteAllBytes(Path.Combine(root, "video.mp4"), new byte[9 << 20]);
                File.WriteAllBytes(Path.Combine(root, "sub", "pacote.zip"), new byte[12 << 20]);
                File.WriteAllBytes(Path.Combine(root, "pequeno.txt"), new byte[10_000]);

                var result = new DiskScanner().Scan(root, null, CancellationToken.None);
                long expected = (9L << 20) + (12L << 20) + 10_000;
                if (result.Root.Size != expected) throw new Exception($"tamanho {result.Root.Size} != {expected}");
                if (result.CategorySizes[(int)FileCategory.Video] != 9L << 20) throw new Exception("categoria Vídeo errada");
                if (result.CategorySizes[(int)FileCategory.Archive] != 12L << 20) throw new Exception("categoria Compactado errada");
                if (result.TopFiles.Count != 2 || result.TopFiles[0].Name != "pacote.zip") throw new Exception("lista de maiores arquivos errada");

                var cells = new List<(DiskNode Node, System.Windows.Rect Rect)>();
                var bounds = new System.Windows.Rect(0, 0, 800, 400);
                TreemapLayout.Layout(result.Root.Children, bounds, cells);
                double area = cells.Sum(c => c.Rect.Width * c.Rect.Height);
                if (Math.Abs(area - bounds.Width * bounds.Height) > bounds.Width * bounds.Height * 0.001) throw new Exception($"mapa cobre {area:0} de {bounds.Width * bounds.Height:0}");
                return $"{result.Files} arquivos, {SizeFormat.Bytes(result.Root.Size)}, mapa cobre {area / (bounds.Width * bounds.Height):P2} da área";
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });

        Check("Espaço: exclusão segura (Lixeira)", () =>
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!SafeDelete.IsProtected(win) || !SafeDelete.IsProtected(Path.Combine(win, "System32", "kernel32.dll")))
                throw new Exception("Windows deveria ser protegido");
            if (!SafeDelete.IsProtected(pf) || !SafeDelete.IsProtected(Path.GetPathRoot(win)!))
                throw new Exception("Program Files e a raiz do disco deveriam ser protegidos");
            if (SafeDelete.IsProtected(Path.Combine(docs, "qualquer.mp4")))
                throw new Exception("Documentos não deveria ser protegido");
            if (SafeDelete.ToRecycleBin(Path.Combine(win, "System32", "kernel32.dll"), false) is null)
                throw new Exception("apagou arquivo do Windows!");

            string name = "otimizador-lixeira-teste-" + Guid.NewGuid().ToString("N")[..8] + ".bin";
            string file = Path.Combine(Path.GetTempPath(), name);
            File.WriteAllBytes(file, new byte[200_000]);
            var error = SafeDelete.ToRecycleBin(file, false);
            if (error is not null) throw new Exception(error);
            if (File.Exists(file)) throw new Exception("arquivo continua no lugar");

            // limpa só o item de teste da Lixeira (não mexe em mais nada)
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -Command \"(New-Object -ComObject Shell.Application).NameSpace(10).Items() | Where-Object { $_.Name -like 'otimizador-lixeira-teste-*' } | ForEach-Object { Remove-Item -LiteralPath $_.Path -Force -ErrorAction SilentlyContinue }\"")
            { UseShellExecute = false, CreateNoWindow = true };
            using (var p = System.Diagnostics.Process.Start(psi)) p?.WaitForExit(30_000);
            return "arquivo foi para a Lixeira; Windows/Program Files/raiz protegidos";
        });
        Check("Espaço: duplicados", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "otimizador-dup-test-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root, "b"));
            try
            {
                var same = new byte[3 << 20];
                new Random(5).NextBytes(same);
                var other = new byte[3 << 20];
                new Random(6).NextBytes(other); // mesmo tamanho, conteúdo diferente
                File.WriteAllBytes(Path.Combine(root, "a.bin"), same);
                File.WriteAllBytes(Path.Combine(root, "b", "copia.bin"), same);
                File.WriteAllBytes(Path.Combine(root, "diferente.bin"), other);

                var scan = new DiskScanner().Scan(root, null, CancellationToken.None);
                var groups = DuplicateFinder.Find(scan.Root, null, CancellationToken.None);
                if (groups.Count != 1 || groups[0].Files.Count != 2) throw new Exception($"{groups.Count} grupos (esperado 1 com 2 arquivos)");
                if (groups[0].Wasted != 3L << 20) throw new Exception("espaço desperdiçado errado");
                return "achou o par idêntico e ignorou o arquivo de mesmo tamanho mas conteúdo diferente";
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });

        Check("Inicialização: ligar e desligar", () =>
        {
            const string Name = "OtimizadorSelfTestEntry";
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            run.SetValue(Name, "cmd.exe /c exit");
            try
            {
                var entry = StartupService.List().FirstOrDefault(e => e.Name == Name) ?? throw new Exception("entrada de teste não apareceu");
                if (!entry.Enabled) throw new Exception("deveria começar ligada");
                if (!StartupService.SetEnabled(entry, false)) throw new Exception("não conseguiu desligar");
                if (StartupService.List().First(e => e.Name == Name).Enabled) throw new Exception("continua ligada");
                if (!StartupService.SetEnabled(entry, true)) throw new Exception("não conseguiu religar");
                if (!StartupService.List().First(e => e.Name == Name).Enabled) throw new Exception("não religou");
                return $"ok ({StartupService.List().Count} programas na inicialização)";
            }
            finally
            {
                run.DeleteValue(Name, false);
                using var approved = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
                approved?.DeleteValue(Name, false);
            }
        });

        Check("Limpeza: só o que é antigo e dentro da pasta", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "otimizador-clean-test-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            var old = Path.Combine(dir, "sub", "velho.tmp");
            var fresh = Path.Combine(dir, "novo.tmp");
            File.WriteAllBytes(old, new byte[200_000]);
            File.WriteAllBytes(fresh, new byte[50_000]);
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-3));
            try
            {
                var cat = new CleanupCategory
                {
                    Id = "teste", NamePt = "t", NameEn = "t", DescPt = "t", DescEn = "t", MinAgeDays = 1, Roots = () => new[] { dir },
                };
                long measured = CleanupService.Measure(cat);
                if (measured != 200_000) throw new Exception($"mediu {measured}, esperado 200000");
                long freed = CleanupService.Clean(cat);
                if (freed != 200_000 || File.Exists(old)) throw new Exception("não apagou o arquivo antigo");
                if (!File.Exists(fresh) || !Directory.Exists(dir)) throw new Exception("apagou o que não devia");
                return "apagou só o arquivo com mais de 1 dia; o recente e a pasta ficaram";
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        });

        Check("Saúde dos discos", () =>
        {
            var disks = Otimizador.App.Hardware.DiskHealthService.Read();
            return disks.Count == 0 ? "sem dados (o Windows não informou)"
                : string.Join("; ", disks.Select(d => $"{d.Name} = {d.Verdict}"));
        });

        Check("Histórico: agrupamento", () =>
        {
            var now = DateTime.Now;
            var pts = new List<HistoryPoint>
            {
                new(now.AddMinutes(-50), 10, 50, 20, 40, 30),
                new(now.AddMinutes(-49), 30, 70, 20, 40, 30),
                new(now.AddMinutes(-5), 50, 60, 20, 40, 30),
            };
            var avg = HistoryService.Bucket(pts, TimeSpan.FromHours(1), 60, p => p.CpuLoad, false);
            var max = HistoryService.Bucket(pts, TimeSpan.FromHours(1), 60, p => p.CpuTemp, true);
            if (avg.Count(v => !double.IsNaN(v)) != 3 || max.Where(v => !double.IsNaN(v)).Max() != 70) throw new Exception("agrupamento errado");
            return "médias e máximas corretas";
        });

        Check("Atualização: leitura da Release e checksum", () =>
        {
            const string json = """
                {"tag_name":"v9.8.7","html_url":"https://github.com/x/y/releases/tag/v9.8.7","body":"Novidades",
                 "assets":[
                  {"name":"Otimizador-Setup-9.8.7.exe","browser_download_url":"https://github.com/x/y/releases/download/v9.8.7/Otimizador-Setup-9.8.7.exe"},
                  {"name":"Otimizador-Portatil-9.8.7.zip","browser_download_url":"https://github.com/x/y/releases/download/v9.8.7/Otimizador-Portatil-9.8.7.zip"},
                  {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/x/y/releases/download/v9.8.7/SHA256SUMS.txt"},
                  {"name":"malicioso.exe","browser_download_url":"http://evil.example.com/Setup.exe"}]}
                """;
            var info = UpdateService.ParseRelease(json) ?? throw new Exception("não leu a Release");
            if (info.Version != new Version(9, 8, 7) || info.InstallerUrl is null || info.ZipUrl is null || info.ChecksumsUrl is null)
                throw new Exception("campos errados");
            if (UpdateService.IsAllowedUrl("http://github.com/a") || UpdateService.IsAllowedUrl("https://evil.example.com/a") || UpdateService.IsAllowedUrl("https://github.com.evil.com/a"))
                throw new Exception("aceitou endereço que não é do GitHub");
            if (!UpdateService.IsAllowedUrl("https://github.com/x/y/releases/download/v1/a.exe") || !UpdateService.IsAllowedUrl("https://objects.githubusercontent.com/a"))
                throw new Exception("recusou endereço do GitHub");
            if (!(new Version(1, 2, 0) > new Version(1, 1, 9))) throw new Exception("comparação de versões");
            var hash = new string('a', 64);
            if (UpdateService.FindHash($"{hash}  Otimizador-Setup-9.8.7.exe\n{new string('b', 64)} *outro.zip", "Otimizador-Setup-9.8.7.exe") != hash)
                throw new Exception("checksum não encontrado");
            if (AppInfo.IsValidRepo("a b/c") || AppInfo.IsValidRepo("../x") || !AppInfo.IsValidRepo("Juan/Otimizador")) throw new Exception("validação do repositório");
            return "lê Release, recusa fora do GitHub, acha o checksum";
        });

        Check("Atualização: consulta real ao GitHub", () =>
        {
            var r = Task.Run(() => UpdateService.CheckAsync(default, "git-for-windows/git")).GetAwaiter().GetResult();
            if (r.State == UpdateState.Error) return $"ignorado (sem acesso ao GitHub agora: {r.Message})";
            if (r.State != UpdateState.Available || r.Info is null) throw new Exception($"estado inesperado: {r.State}");
            return $"API do GitHub respondeu: última versão do repositório de teste = {r.Info.Tag}";
        });

        Check("Atualização: Release real do projeto (baixar e conferir)", () =>
        {
            var r = Task.Run(() => UpdateService.CheckAsync(default, AppInfo.DefaultRepo)).GetAwaiter().GetResult();
            if (r.State is UpdateState.Error or UpdateState.NoRelease) return $"ignorado ({r.State}: {r.Message})";
            var info = r.Info ?? throw new Exception("sem dados da Release");
            if (info.InstallerUrl is null || info.ZipUrl is null || info.ChecksumsUrl is null)
                throw new Exception("a Release não tem instalador, zip e SHA256SUMS.txt");
            var path = Task.Run(() => UpdateService.DownloadAsync(info, info.InstallerUrl, null, CancellationToken.None)).GetAwaiter().GetResult();
            var size = new FileInfo(path).Length / 1048576;
            File.Delete(path);
            return $"{info.Tag} ({r.State}): baixou o instalador ({size} MB) e o SHA-256 bateu";
        });
        Check("Limpeza agendada: próximo horário", () =>
        {
            var last = new DateTime(2026, 10, 23, 15, 0, 0); // sexta
            var next = ScheduledCleanupService.NextDue(last, (int)DayOfWeek.Sunday, 12);
            if (next != new DateTime(2026, 10, 25, 12, 0, 0)) throw new Exception($"deu {next}");
            var sameDay = ScheduledCleanupService.NextDue(new DateTime(2026, 10, 25, 9, 0, 0), (int)DayOfWeek.Sunday, 12);
            if (sameDay != new DateTime(2026, 10, 25, 12, 0, 0)) throw new Exception($"mesmo dia deu {sameDay}");
            var afterSame = ScheduledCleanupService.NextDue(new DateTime(2026, 10, 25, 13, 0, 0), (int)DayOfWeek.Sunday, 12);
            if (afterSame != new DateTime(2026, 11, 1, 12, 0, 0)) throw new Exception($"depois do horário deu {afterSame}");
            return "domingo 12:00 calculado certo nos 3 casos";
        });

        Check("Segurança: leitura dos resultados", () =>
        {
            const string good = """{"defender":{"enabled":true,"realtime":true,"sigAge":1},"av":[{"name":"Windows Defender","on":true}],"firewall":[{"name":"Domain","on":true},{"name":"Public","on":true}],"bitlocker":{"on":true},"secureboot":true,"tpm":{"present":true,"ready":true},"uac":1,"reboot":false}""";
            const string bad = """{"defender":null,"av":[],"firewall":{"name":"Public","on":false},"bitlocker":null,"secureboot":false,"uac":0,"reboot":true}""";
            var g = SecurityService.Parse(good);
            if (g.Any(i => i.Level != SecLevel.Good)) throw new Exception("tudo ok deveria dar verde");
            var b = SecurityService.Parse(bad);
            if (b.First(i => i.Key == "av").Level != SecLevel.Bad || b.First(i => i.Key == "uac").Level != SecLevel.Bad
                || b.First(i => i.Key == "fw").Level != SecLevel.Bad || b.First(i => i.Key == "reboot").Level != SecLevel.Warning)
                throw new Exception("problemas não foram detectados");
            var updates = SecurityService.ParseUpdates("""{"count":3,"titles":["KB1","KB2","KB3"]}""");
            if (updates.Level != SecLevel.Warning) throw new Exception("atualizações pendentes");
            return "marca verde, amarelo e vermelho corretamente";
        });

        Check("Segurança: verificação real deste PC", () =>
        {
            var items = SecurityService.Check();
            if (items.Count < 3) throw new Exception($"só {items.Count} itens");
            var (good, total) = SecurityService.Score(items);
            return $"{good}/{total} itens verdes: " + string.Join(", ", items.Select(i => $"{i.Key}={i.Level}"));
        });

        Check("Inventário: publicar e ler", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "otimizador-inv-test-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var (ok, message) = Task.Run(() => InventoryService.PublishAsync(folder)).GetAwaiter().GetResult();
                if (!ok) throw new Exception(message);
                var all = InventoryService.ReadAll(folder);
                var me = all.FirstOrDefault(r => r.Machine == Environment.MachineName) ?? throw new Exception("não achou este PC");
                if (me.Cpu.Length == 0 || me.Disks.Count == 0) throw new Exception("dados incompletos");
                return $"{me.Machine}: {me.Cpu}, {me.RamGb:0} GB, {me.Disks.Count} disco(s), inicialização {me.StartupEnabled}/{me.StartupTotal}";
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        });

        Check("Rede: velocidade e conexões", () =>
        {
            var net = new NetworkMonitor();
            Task.Run(() => net.SampleAsync()).GetAwaiter().GetResult();
            Thread.Sleep(1100);
            var s = Task.Run(() => net.SampleAsync()).GetAwaiter().GetResult();
            if (s.DownBytesPerSec < 0 || s.UpBytesPerSec < 0) throw new Exception("velocidade negativa");
            var top = NetworkMonitor.TopConnections();
            return $"{NetworkMonitor.FormatSpeed(s.DownBytesPerSec)} baixando, ping {(s.PingMs is { } p ? $"{p:0} ms" : "sem resposta")}, {top.Count} programa(s) com conexões ({string.Join(", ", top.Take(3).Select(t => t.Name))})";
        });

        Check("Bateria e modo jogo (detecção)", () =>
        {
            var bat = Otimizador.App.Hardware.BatteryHealthService.Read();
            var game = GameModeService.DetectFullscreenGame();
            return (bat is null ? "sem bateria (desktop)" : $"bateria {bat.HealthPercent:0}%") + $"; jogo em tela cheia agora: {game ?? "nenhum"}";
        });
        Check("Relatório HTML", () =>
        {
            var html = Task.Run(() => ReportService.BuildHtmlAsync()).GetAwaiter().GetResult(); // fora da thread da interface (evita impasse)
            foreach (var part in new[] { "<h1>", "Saúde dos discos", "Programas na inicialização", "</html>" })
                if (!html.Contains(part)) throw new Exception($"faltou \"{part}\"");
            var path = Path.Combine(Path.GetTempPath(), "otimizador-relatorio-teste.html");
            File.WriteAllText(path, html);
            File.Delete(path);
            return $"{html.Length / 1024} KB gerados";
        });
        Check("Apps pré-instalados: detecção", () => $"{PreinstalledApps.Installed().Count} da lista estão instalados");

        foreach (var id in new[] { "menudelay", "transparency" })
        {
            Check($"Ajuste novo: {id} aplicar e desfazer", () =>
            {
                var tweak = TweakCatalog.All.First(t => t.Id == id);
                var before = tweak.IsApplied();
                tweak.Apply(TweakStateStore.Load());
                if (!tweak.IsApplied()) throw new Exception("não ficou aplicado");
                if (!tweak.Revert(TweakStateStore.Load())) throw new Exception("sem estado para desfazer");
                if (before != tweak.IsApplied()) throw new Exception("estado não voltou ao original");
                return "ok";
            });
        }
        Check("Ajustes: detecção de todos", () =>
        {
            return string.Join(", ", TweakCatalog.All.Select(t => $"{t.Id}={(t.IsApplied() ? "on" : "off")}"));
        });

        Check("Idiomas", () =>
        {
            var missing = new List<string>();
            foreach (var (k, v) in Strings.Table)
                if (string.IsNullOrWhiteSpace(v.Pt) || string.IsNullOrWhiteSpace(v.En)) missing.Add(k);
            if (missing.Count > 0) throw new Exception("textos vazios: " + string.Join(", ", missing));
            return $"{Strings.Table.Count} textos PT/EN";
        });

        sb.AppendLine().AppendLine(failures == 0 ? "RESULTADO: TUDO OK" : $"RESULTADO: {failures} FALHA(S)");
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        return failures == 0 ? 0 : 1;
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}












