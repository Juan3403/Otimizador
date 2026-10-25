using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Otimizador.App.Bench;

public sealed record BenchResult(string Key, double Value, string Unit);

/// <summary>Benchmarks próprios de CPU, memória e disco. Cada teste devolve uma pontuação (MB/s ou GB/s).</summary>
public static class Benchmarks
{
    private static double HashLoop(TimeSpan duration, CancellationToken ct)
    {
        var buffer = new byte[1024 * 1024];
        new Random(42).NextBytes(buffer);
        long bytes = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration && !ct.IsCancellationRequested)
        {
            SHA256.HashData(buffer);
            bytes += buffer.Length;
        }
        return bytes / 1024d / 1024d / sw.Elapsed.TotalSeconds;
    }

    public static BenchResult CpuSingle(TimeSpan duration, CancellationToken ct) =>
        new("Test_CpuSingle", HashLoop(duration, ct), "MB/s");

    public static BenchResult CpuMulti(TimeSpan duration, CancellationToken ct)
    {
        var results = new double[Environment.ProcessorCount];
        var threads = Enumerable.Range(0, results.Length).Select(i =>
        {
            var t = new Thread(() => results[i] = HashLoop(duration, ct)) { IsBackground = true };
            t.Start();
            return t;
        }).ToList();
        threads.ForEach(t => t.Join());
        return new("Test_CpuMulti", results.Sum(), "MB/s");
    }

    public static BenchResult Memory(CancellationToken ct, int megabytes = 128, int passes = 8)
    {
        var src = new byte[megabytes * 1024 * 1024];
        var dst = new byte[src.Length];
        new Random(7).NextBytes(src);
        Buffer.BlockCopy(src, 0, dst, 0, src.Length); // aquecimento
        var sw = Stopwatch.StartNew();
        int done = 0;
        for (; done < passes && !ct.IsCancellationRequested; done++)
            Buffer.BlockCopy(src, 0, dst, 0, src.Length);
        double gb = (double)src.Length * done / 1024 / 1024 / 1024;
        return new("Test_Ram", gb / sw.Elapsed.TotalSeconds, "GB/s");
    }

    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    /// <summary>Disco sem cache do Windows (FILE_FLAG_NO_BUFFERING), com buffer alinhado a 4 KB.</summary>
    public static unsafe (BenchResult Write, BenchResult Read) Disk(CancellationToken ct, int megabytes = 256)
    {
        const int ChunkSize = 4 * 1024 * 1024;
        var path = Path.Combine(Path.GetTempPath(), $"otimizador-bench-{Guid.NewGuid():N}.tmp");
        void* mem = NativeMemory.AlignedAlloc((nuint)ChunkSize, 4096);
        try
        {
            var chunk = new Span<byte>(mem, ChunkSize);
            new Random(1).NextBytes(chunk);
            int chunks = megabytes / 4;

            var sw = Stopwatch.StartNew();
            using (var h = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, NoBuffering | FileOptions.WriteThrough))
            {
                for (int i = 0; i < chunks && !ct.IsCancellationRequested; i++)
                    RandomAccess.Write(h, chunk, (long)i * ChunkSize);
            }
            double write = megabytes / sw.Elapsed.TotalSeconds;

            sw.Restart();
            long read = 0;
            using (var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None, NoBuffering | FileOptions.SequentialScan))
            {
                int n;
                while (!ct.IsCancellationRequested && (n = RandomAccess.Read(h, chunk, read)) > 0)
                    read += n;
            }
            double readSpeed = read / 1024d / 1024d / sw.Elapsed.TotalSeconds;
            return (new("Test_DiskW", write, "MB/s"), new("Test_DiskR", readSpeed, "MB/s"));
        }
        finally
        {
            NativeMemory.AlignedFree(mem);
            try
            {
                File.Delete(path);
            }
            catch
            {
                // arquivo temporário; o Windows limpa depois
            }
        }
    }
}
