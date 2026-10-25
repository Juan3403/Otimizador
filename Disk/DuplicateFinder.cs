using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Otimizador.App.Disk;

public sealed record DuplicateGroup(long Size, List<DiskNode> Files)
{
    /// <summary>Espaço que se ganha mantendo só uma cópia.</summary>
    public long Wasted => Size * (Files.Count - 1);
}

/// <summary>
/// Acha arquivos idênticos (mesmo conteúdo) entre os arquivos grandes da análise: primeiro agrupa por tamanho,
/// depois compara um resumo do começo e do fim e, só para os que ainda empatam, o conteúdo inteiro (SHA-256).
/// Arquivos do Windows e de programas instalados são ignorados.
/// </summary>
public static class DuplicateFinder
{
    private static void Collect(DiskNode node, List<DiskNode> files)
    {
        foreach (var c in node.Children)
        {
            if (c.IsOther) continue;
            if (c.IsDir) Collect(c, files);
            else files.Add(c);
        }
    }

    private static FileStream? Open(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        }
        catch
        {
            return null;
        }
    }

    private static string? PartialHash(string path, long size)
    {
        using var fs = Open(path);
        if (fs is null) return null;
        try
        {
            const int Chunk = 64 * 1024;
            var buffer = new byte[Chunk * 2];
            int n = fs.Read(buffer, 0, Chunk);
            if (size > Chunk * 2)
            {
                fs.Seek(-Chunk, SeekOrigin.End);
                n += fs.Read(buffer, n, Chunk);
            }
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, n)));
        }
        catch
        {
            return null;
        }
    }

    private static string? FullHash(string path, CancellationToken ct)
    {
        using var fs = Open(path);
        if (fs is null) return null;
        try
        {
            using var sha = SHA256.Create();
            var buffer = new byte[1 << 20];
            int n;
            while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, n, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static List<DuplicateGroup> Find(DiskNode root, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var all = new List<DiskNode>();
        Collect(root, all);
        var candidates = all
            .Where(f => f.Size > 0 && !SafeDelete.IsProtected(f.FullPath))
            .GroupBy(f => f.Size)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();

        int total = candidates.Sum(g => g.Count), done = 0;
        var found = new ConcurrentBag<DuplicateGroup>();
        progress?.Report((0, total));

        Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, group =>
        {
            var partial = new Dictionary<string, List<DiskNode>>();
            foreach (var f in group)
            {
                ct.ThrowIfCancellationRequested();
                var h = PartialHash(f.FullPath, f.Size);
                if (h is null) continue;
                if (!partial.TryGetValue(h, out var l)) partial[h] = l = new List<DiskNode>();
                l.Add(f);
            }

            foreach (var same in partial.Values.Where(v => v.Count > 1))
            {
                var full = new Dictionary<string, List<DiskNode>>();
                foreach (var f in same)
                {
                    var h = FullHash(f.FullPath, ct);
                    if (h is null) continue;
                    if (!full.TryGetValue(h, out var l)) full[h] = l = new List<DiskNode>();
                    l.Add(f);
                }
                foreach (var dup in full.Values.Where(v => v.Count > 1))
                    found.Add(new DuplicateGroup(dup[0].Size, dup.OrderBy(f => f.Modified).ToList()));
            }

            int d = Interlocked.Add(ref done, group.Count);
            progress?.Report((d, total));
        });

        return found.OrderByDescending(g => g.Wasted).ToList();
    }
}
