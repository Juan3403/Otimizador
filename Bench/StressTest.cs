using System.Diagnostics;
using System.Security.Cryptography;
using Otimizador.App.Hardware;

namespace Otimizador.App.Bench;

public sealed record StressStatus(
    TimeSpan Elapsed, long Iterations, long Errors, float? CpuTemp, float? MaxTemp, float? CpuLoad = null, bool Final = false);

public enum StressOutcome { Passed, Failed, Aborted, Cancelled }

/// <summary>
/// Estabilidade: todos os núcleos calculam uma cadeia de hashes cujo resultado é conhecido;
/// uma thread extra grava e confere padrões na RAM. Qualquer divergência conta como erro.
/// </summary>
public static class StressTest
{
    public const float TempLimit = 100f;

    public static StressOutcome Run(TimeSpan duration, SensorService? sensors, IProgress<StressStatus> progress, CancellationToken ct)
    {
        long iterations = 0, errors = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = new List<Thread>();

        int cpuThreads = Math.Max(1, Environment.ProcessorCount - 1);
        for (int i = 0; i < cpuThreads; i++)
        {
            int seed = i;
            var t = new Thread(() => CpuWorker(seed, stop.Token, ref iterations, ref errors)) { IsBackground = true };
            workers.Add(t);
        }
        workers.Add(new Thread(() => MemoryWorker(stop.Token, ref iterations, ref errors)) { IsBackground = true });
        workers.ForEach(t => t.Start());

        float? maxTemp = null;
        bool aborted = false;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            Thread.Sleep(1000);
            var snap = sensors?.Snapshot();
            float? temp = snap is null ? null : SensorService.CpuTemp(snap);
            float? load = snap is null ? null : SensorService.CpuLoad(snap);
            if (temp is not null) maxTemp = Math.Max(maxTemp ?? 0, temp.Value);
            progress.Report(new StressStatus(sw.Elapsed, Interlocked.Read(ref iterations), Interlocked.Read(ref errors), temp, maxTemp, load));
            if (temp is >= TempLimit)
            {
                aborted = true;
                break;
            }
        }

        stop.Cancel();
        workers.ForEach(t => t.Join());
        progress.Report(new StressStatus(sw.Elapsed, iterations, errors, null, maxTemp, null, Final: true));

        if (aborted) return StressOutcome.Aborted;
        if (errors > 0) return StressOutcome.Failed;
        return ct.IsCancellationRequested ? StressOutcome.Cancelled : StressOutcome.Passed;
    }

    private static byte[] Chain(byte[] input)
    {
        var h = input;
        for (int i = 0; i < 2000; i++) h = SHA256.HashData(h);
        return h;
    }

    private static void CpuWorker(int seed, CancellationToken ct, ref long iterations, ref long errors)
    {
        var input = new byte[64];
        new Random(seed).NextBytes(input);
        var expected = Chain(input);
        while (!ct.IsCancellationRequested)
        {
            var got = Chain(input);
            if (!got.AsSpan().SequenceEqual(expected)) Interlocked.Increment(ref errors);
            Interlocked.Increment(ref iterations);
        }
    }

    private static void MemoryWorker(CancellationToken ct, ref long iterations, ref long errors)
    {
        var block = new ulong[32 * 1024 * 1024 / sizeof(ulong)]; // 32 MB
        ulong[] patterns = { 0xAAAAAAAAAAAAAAAA, 0x5555555555555555, 0xFFFFFFFFFFFFFFFF, 0x0000000000000000 };
        while (!ct.IsCancellationRequested)
        {
            foreach (var p in patterns)
            {
                Array.Fill(block, p);
                for (int i = 0; i < block.Length; i++)
                {
                    if (block[i] != p)
                    {
                        Interlocked.Increment(ref errors);
                        break;
                    }
                }
                Interlocked.Increment(ref iterations);
                if (ct.IsCancellationRequested) return;
            }
        }
    }
}
