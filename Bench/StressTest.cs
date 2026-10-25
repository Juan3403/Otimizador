using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using Otimizador.App.Hardware;

namespace Otimizador.App.Bench;

/// <summary>Peças que podem ser testadas, sozinhas ou combinadas.</summary>
[Flags]
public enum StressTargets
{
    None = 0,
    Cpu = 1,
    Memory = 2,
    Gpu = 4,
    Disk = 8,
    All = Cpu | Memory | Gpu | Disk,
}

public sealed record StressStatus(
    TimeSpan Elapsed, long Iterations, long Errors, float? CpuTemp, float? MaxTemp, float? CpuLoad = null, bool Final = false,
    float? GpuTemp = null, float? MaxGpuTemp = null, float? GpuLoad = null,
    StressTargets FailedTargets = StressTargets.None, bool GpuMissing = false);

public enum StressOutcome { Passed, Failed, Aborted, Cancelled }

/// <summary>
/// Estabilidade por peça: a CPU calcula uma cadeia de hashes de resultado conhecido, a RAM grava e confere padrões,
/// a GPU repete um cálculo de shader e compara com o primeiro resultado, o disco grava e relê um arquivo temporário.
/// Qualquer divergência conta como erro daquela peça.
/// </summary>
public static class StressTest
{
    public const float TempLimit = 100f;
    public const float GpuTempLimit = 95f;

    public static StressOutcome Run(TimeSpan duration, SensorService? sensors, IProgress<StressStatus> progress, CancellationToken ct,
        StressTargets targets = StressTargets.Cpu | StressTargets.Memory)
    {
        long iterations = 0;
        var errors = new long[4]; // CPU, RAM, GPU, disco
        long gpuMissing = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = new List<Thread>();

        if (targets.HasFlag(StressTargets.Cpu))
        {
            int cpuThreads = Math.Max(1, Environment.ProcessorCount - 1);
            for (int i = 0; i < cpuThreads; i++)
            {
                int seed = i;
                workers.Add(new Thread(() => CpuWorker(seed, stop.Token, ref iterations, ref errors[0])) { IsBackground = true });
            }
        }
        if (targets.HasFlag(StressTargets.Memory))
            workers.Add(new Thread(() => MemoryWorker(stop.Token, ref iterations, ref errors[1])) { IsBackground = true });
        if (targets.HasFlag(StressTargets.Gpu))
            workers.Add(new Thread(() =>
            {
                bool ok = GpuBenchmark.StressLoop(stop.Token,
                    () => Interlocked.Increment(ref iterations),
                    () => Interlocked.Increment(ref errors[2]));
                if (!ok) Interlocked.Exchange(ref gpuMissing, 1);
            }) { IsBackground = true });
        if (targets.HasFlag(StressTargets.Disk))
            workers.Add(new Thread(() => DiskWorker(stop.Token, ref iterations, ref errors[3])) { IsBackground = true });
        workers.ForEach(t => t.Start());

        float? maxTemp = null, maxGpuTemp = null;
        bool aborted = false;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            Thread.Sleep(1000);
            var snap = sensors?.Snapshot();
            float? temp = snap is null ? null : SensorService.CpuTemp(snap);
            float? load = snap is null ? null : SensorService.CpuLoad(snap);
            float? gTemp = snap is null ? null : SensorService.GpuTemp(snap);
            float? gLoad = snap is null ? null : SensorService.GpuLoad(snap);
            if (temp is not null) maxTemp = Math.Max(maxTemp ?? 0, temp.Value);
            if (gTemp is not null) maxGpuTemp = Math.Max(maxGpuTemp ?? 0, gTemp.Value);
            progress.Report(new StressStatus(sw.Elapsed, Interlocked.Read(ref iterations), errors.Sum(), temp, maxTemp, load,
                false, gTemp, maxGpuTemp, gLoad, Failed(errors), Interlocked.Read(ref gpuMissing) == 1));
            if (temp is >= TempLimit || (targets.HasFlag(StressTargets.Gpu) && gTemp is >= GpuTempLimit))
            {
                aborted = true;
                break;
            }
        }

        stop.Cancel();
        workers.ForEach(t => t.Join());
        progress.Report(new StressStatus(sw.Elapsed, iterations, errors.Sum(), null, maxTemp, null, true,
            null, maxGpuTemp, null, Failed(errors), Interlocked.Read(ref gpuMissing) == 1));

        if (aborted) return StressOutcome.Aborted;
        if (errors.Sum() > 0) return StressOutcome.Failed;
        return ct.IsCancellationRequested ? StressOutcome.Cancelled : StressOutcome.Passed;
    }

    private static StressTargets Failed(long[] errors)
    {
        var f = StressTargets.None;
        if (Interlocked.Read(ref errors[0]) > 0) f |= StressTargets.Cpu;
        if (Interlocked.Read(ref errors[1]) > 0) f |= StressTargets.Memory;
        if (Interlocked.Read(ref errors[2]) > 0) f |= StressTargets.Gpu;
        if (Interlocked.Read(ref errors[3]) > 0) f |= StressTargets.Disk;
        return f;
    }

    private static byte[] Chain(byte[] input)
    {
        var h = input;
        for (int i = 0; i < 2000; i++) h = SHA256.HashData(h);
        return h;
    }

    /// <summary>Carga de instruções vetoriais (AVX2 + FMA): é a que mais esquenta e mais exige da fonte de energia.</summary>
    private static Vector256<float> AvxKernel()
    {
        var b = Vector256.Create(0.999999f);
        var c = Vector256.Create(0.000001f);
        var a0 = Vector256.Create(0.1f); var a1 = Vector256.Create(0.2f); var a2 = Vector256.Create(0.3f); var a3 = Vector256.Create(0.4f);
        var a4 = Vector256.Create(0.5f); var a5 = Vector256.Create(0.6f); var a6 = Vector256.Create(0.7f); var a7 = Vector256.Create(0.8f);
        for (int i = 0; i < 400_000; i++)
        {
            a0 = Fma.MultiplyAdd(a0, b, c); a1 = Fma.MultiplyAdd(a1, b, c); a2 = Fma.MultiplyAdd(a2, b, c); a3 = Fma.MultiplyAdd(a3, b, c);
            a4 = Fma.MultiplyAdd(a4, b, c); a5 = Fma.MultiplyAdd(a5, b, c); a6 = Fma.MultiplyAdd(a6, b, c); a7 = Fma.MultiplyAdd(a7, b, c);
        }
        return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7;
    }

    private static void AvxWorker(CancellationToken ct, ref long iterations, ref long errors)
    {
        var expected = AvxKernel();
        while (!ct.IsCancellationRequested)
        {
            if (AvxKernel() != expected) Interlocked.Increment(ref errors);
            Interlocked.Increment(ref iterations);
        }
    }

    private static void CpuWorker(int seed, CancellationToken ct, ref long iterations, ref long errors)
    {
        if (seed % 2 == 1 && Fma.IsSupported)
        {
            AvxWorker(ct, ref iterations, ref errors);
            return;
        }
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

    /// <summary>Grava 64 MB de dados conhecidos num arquivo temporário (direto no disco) e relê comparando.</summary>
    private static void DiskWorker(CancellationToken ct, ref long iterations, ref long errors)
    {
        const int size = 64 * 1024 * 1024;
        string path = Path.Combine(Path.GetTempPath(), $"otimizador-estresse-{Guid.NewGuid():N}.tmp");
        var data = new byte[size];
        var readBack = new byte[size];
        new Random(42).NextBytes(data);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var w = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough))
                    {
                        w.Write(data, 0, size);
                        w.Flush(true);
                    }
                    if (ct.IsCancellationRequested) return;
                    using (var r = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1 << 20, FileOptions.SequentialScan))
                    {
                        int got = 0;
                        while (got < size)
                        {
                            int n = r.Read(readBack, got, size - got);
                            if (n <= 0) break;
                            got += n;
                        }
                        if (got != size || !data.AsSpan().SequenceEqual(readBack)) Interlocked.Increment(ref errors);
                    }
                    Interlocked.Increment(ref iterations);
                    ct.WaitHandle.WaitOne(400); // limita as gravações para não gastar o SSD à toa
                }
                catch (IOException)
                {
                    Interlocked.Increment(ref errors); // disco cheio ou falha de E/S
                    return;
                }
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* arquivo temporário: o Windows limpa depois */ }
        }
    }
}
