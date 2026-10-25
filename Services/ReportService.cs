using System.Globalization;
using System.Net;
using System.Text;
using Otimizador.App.Bench;
using Otimizador.App.Boot;
using Otimizador.App.Disk;
using Otimizador.App.Hardware;

namespace Otimizador.App;

/// <summary>Relatório em HTML (abre em qualquer navegador e dá para imprimir/salvar em PDF) com o estado do PC.</summary>
public static class ReportService
{
    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>Gráfico SVG simples (0–100) com uma ou mais séries sobrepostas.</summary>
    private static string Svg(params (double[] Values, string Color)[] series)
    {
        const int W = 720, Hh = 90;
        var sb = new StringBuilder();
        foreach (var (values, color) in series)
        {
            var seg = new List<string>();
            int n = values.Length;
            void Flush()
            {
                if (seg.Count > 1) sb.Append($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" points=\"{string.Join(' ', seg)}\"/>");
                seg.Clear();
            }
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(values[i]))
                {
                    Flush();
                    continue;
                }
                double x = n <= 1 ? 0 : i * (double)W / (n - 1);
                double y = Hh - Math.Clamp(values[i], 0, 100) / 100 * (Hh - 4) - 2;
                seg.Add($"{x.ToString("0.#", CultureInfo.InvariantCulture)},{y.ToString("0.#", CultureInfo.InvariantCulture)}");
            }
            Flush();
        }
        return $"<svg viewBox=\"0 0 {W} {Hh}\" width=\"100%\" height=\"{Hh}\" preserveAspectRatio=\"none\"><rect width=\"{W}\" height=\"{Hh}\" fill=\"#f5f5f5\"/>{sb}</svg>";
    }
    public static async Task<string> BuildHtmlAsync()
    {
        var sections = await HardwareInfoService.GetAsync();
        var disks = await Task.Run(DiskHealthService.Read);
        var startup = await Task.Run(StartupService.List);
        var history = Task.Run(() => HistoryService.Instance.Read(TimeSpan.FromHours(24))).Result;
        var bench = BenchHistory.Load();
        var events = EventLog.Read(DateTime.Now.AddDays(7), 60);

        var range = TimeSpan.FromHours(24);
        var cpuLoad = HistoryService.Bucket(history, range, 240, p => p.CpuLoad, false);
        var cpuTemp = HistoryService.Bucket(history, range, 240, p => p.CpuTemp, true);
        var gpuTemp = HistoryService.Bucket(history, range, 240, p => p.GpuTemp, true);
        var ram = HistoryService.Bucket(history, range, 240, p => p.Ram, false);

        string Max(IEnumerable<double> v) => v.Where(x => !double.IsNaN(x)).DefaultIfEmpty(double.NaN).Max() is var m && !double.IsNaN(m) ? $"{m:0} °C" : "—";
        string Avg(IEnumerable<double> v) => v.Where(x => !double.IsNaN(x)).DefaultIfEmpty(double.NaN).Average() is var m && !double.IsNaN(m) ? $"{m:0}%" : "—";

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>Otimizador - Relatório</title><style>");
        sb.Append("body{font:14px Consolas,'Cascadia Mono',monospace;color:#000;background:#fff;max-width:860px;margin:32px auto;padding:0 20px}");
        sb.Append("h1{font-size:24px;margin:0 0 4px}h2{font-size:16px;border-bottom:2px solid #000;padding-bottom:4px;margin-top:28px}");
        sb.Append("table{border-collapse:collapse;width:100%}td,th{border:1px solid #bbb;padding:5px 8px;text-align:left;vertical-align:top}th{background:#eee}");
        sb.Append(".muted{color:#555}.ok{color:#15803d}.warn{color:#b45309}.bad{color:#b91c1c}@media print{body{margin:0}}");
        sb.Append("</style></head><body>");
        sb.Append($"<h1>Otimizador — Relatório</h1><div class=\"muted\">{DateTime.Now:dd/MM/yyyy HH:mm} · {H(Environment.MachineName)}</div>");

        sb.Append("<h2>Computador</h2><table>");
        foreach (var sec in sections)
        {
            sb.Append($"<tr><th colspan=\"2\">{H(sec.Title)}</th></tr>");
            foreach (var l in sec.Lines) sb.Append($"<tr><td width=\"30%\">{H(l.Label)}</td><td>{H(l.Value)}</td></tr>");
        }
        sb.Append("</table>");

        sb.Append("<h2>Saúde dos discos</h2>");
        if (disks.Count == 0) sb.Append("<p class=\"muted\">Sem dados.</p>");
        else
        {
            sb.Append("<table><tr><th>Disco</th><th>Tipo</th><th>Estado</th><th>Temp.</th><th>Desgaste</th><th>Horas ligado</th><th>Erros</th></tr>");
            foreach (var d in disks)
            {
                string cls = d.Level switch { HealthLevel.Good => "ok", HealthLevel.Warning => "warn", HealthLevel.Bad => "bad", _ => "muted" };
                sb.Append($"<tr><td>{H(d.Name)}</td><td>{H(d.Kind)} · {SizeFormat.Bytes(d.SizeBytes)}</td><td class=\"{cls}\">{H(d.Verdict)}</td>");
                sb.Append($"<td>{(d.TemperatureC is { } t ? $"{t:0} °C" : "—")}</td><td>{(d.WearPercent is { } w ? $"{w:0}%" : "—")}</td>");
                sb.Append($"<td>{(d.PowerOnHours is { } h ? $"{h:N0}" : "—")}</td><td>{(d.ReadErrors ?? 0) + (d.WriteErrors ?? 0)}</td></tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("<h2>Últimas 24 horas</h2>");
        if (history.Count < 2) sb.Append("<p class=\"muted\">Ainda não há histórico suficiente. O app grava uma medição por minuto enquanto está aberto.</p>");
        else
        {
            sb.Append($"<p>Temperatura máx. CPU: <b>{Max(cpuTemp)}</b> · GPU: <b>{Max(gpuTemp)}</b> · Uso médio CPU: <b>{Avg(cpuLoad)}</b> · RAM média: <b>{Avg(ram)}</b></p>");
            sb.Append("<div class=\"muted\">CPU: uso (preto) e temperatura (laranja)</div>" + Svg((cpuLoad, "#000"), (cpuTemp, "#d97706")));
            sb.Append("<div class=\"muted\" style=\"margin-top:10px\">Temperatura da GPU</div>" + Svg((gpuTemp, "#d97706")));
            sb.Append("<div class=\"muted\" style=\"margin-top:10px\">Memória RAM (uso)</div>" + Svg((ram, "#555")));
        }

        sb.Append("<h2>Últimos testes</h2>");
        if (bench.Count == 0) sb.Append("<p class=\"muted\">Nenhum teste executado.</p>");
        else
        {
            sb.Append("<table><tr><th>Teste</th><th>Resultado</th></tr>");
            foreach (var (key, value) in bench)
                sb.Append($"<tr><td>{H(Loc.T(key))}</td><td>{value:0.0}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append($"<h2>Programas na inicialização ({startup.Count(s => s.Enabled)} ativos de {startup.Count})</h2><table><tr><th>Programa</th><th>Estado</th><th>Empresa</th></tr>");
        foreach (var s in startup)
            sb.Append($"<tr><td>{H(s.Name)}</td><td class=\"{(s.Enabled ? "ok" : "muted")}\">{(s.Enabled ? "ativo" : "desligado")}</td><td>{H(s.Publisher)}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>O que o Otimizador fez (últimos 7 dias)</h2>");
        if (events.Count == 0) sb.Append("<p class=\"muted\">Nada registrado.</p>");
        else
        {
            sb.Append("<table><tr><th>Quando</th><th>Tipo</th><th>O quê</th></tr>");
            foreach (var e in events) sb.Append($"<tr><td>{e.Time:dd/MM HH:mm}</td><td>{H(e.Kind)}</td><td>{H(e.Text)}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }
}

