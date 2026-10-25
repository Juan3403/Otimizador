using System.Diagnostics;

namespace Otimizador.App.Disk;

public sealed record ScanProgress(long Files, long Bytes, string CurrentPath);

public sealed class ScanResult
{
    public required DiskNode Root { get; init; }

    /// <summary>Bytes por categoria (índice = FileCategory), contando TODOS os arquivos, inclusive os pequenos.</summary>
    public required long[] CategorySizes { get; init; }

    /// <summary>Maiores arquivos encontrados, do maior para o menor.</summary>
    public required List<DiskNode> TopFiles { get; init; }

    public long Files { get; init; }
    public long Bytes { get; init; }
}

/// <summary>
/// Varre um disco/pasta e monta a árvore de tamanhos. Arquivos e pastas com menos de 1 MB não viram nós
/// (seu tamanho entra no total e num nó "pequenos"), para a árvore caber na memória mesmo em discos com milhões de arquivos.
/// Atalhos/junções e arquivos só da nuvem (pontos de reanálise) são ignorados, evitando laços e dupla contagem.
/// </summary>
public sealed class DiskScanner
{
    private const long KeepBytes = 1L << 20;
    private const long TopMinBytes = 8L << 20;

    private readonly long[] _categories = new long[Enum.GetValues<FileCategory>().Length];
    private readonly object _topLock = new();
    private readonly List<DiskNode> _top = new();
    private long _topFloor = TopMinBytes;
    private long _files, _bytes, _lastReportTick;
    private string _current = "";
    private IProgress<ScanProgress>? _progress;
    private CancellationToken _ct;

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public ScanResult Scan(string rootPath, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        _progress = progress;
        _ct = ct;
        var root = new DiskNode { Name = rootPath, FullPath = rootPath, IsDir = true };
        ScanDir(rootPath, root, depth: 0);
        ct.ThrowIfCancellationRequested();

        List<DiskNode> top;
        lock (_topLock) top = _top.OrderByDescending(n => n.Size).Take(300).ToList();
        _progress?.Report(new ScanProgress(Interlocked.Read(ref _files), Interlocked.Read(ref _bytes), rootPath));

        return new ScanResult
        {
            Root = root,
            CategorySizes = _categories,
            TopFiles = top,
            Files = _files,
            Bytes = _bytes,
        };
    }

    private void Report()
    {
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref _lastReportTick);
        if (now - last < 200 || Interlocked.CompareExchange(ref _lastReportTick, now, last) != last) return;
        _progress?.Report(new ScanProgress(Interlocked.Read(ref _files), Interlocked.Read(ref _bytes), _current));
    }

    private void AddTop(DiskNode file)
    {
        lock (_topLock)
        {
            if (file.Size < _topFloor) return;
            _top.Add(file);
            if (_top.Count > 800)
            {
                _top.Sort((a, b) => b.Size.CompareTo(a.Size));
                _top.RemoveRange(400, _top.Count - 400);
                _topFloor = _top[^1].Size;
            }
        }
    }

    private void ScanDir(string path, DiskNode node, int depth)
    {
        _ct.ThrowIfCancellationRequested();
        _current = path;

        long small = 0, smallCount = 0;
        var subdirs = new List<DirectoryInfo>();
        try
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Options))
            {
                if (entry is DirectoryInfo d)
                {
                    subdirs.Add(d);
                    continue;
                }

                long len;
                try
                {
                    len = ((FileInfo)entry).Length;
                }
                catch
                {
                    continue;
                }

                var cat = FileCategories.Of(entry.Name);
                Interlocked.Increment(ref _files);
                Interlocked.Add(ref _bytes, len);
                Interlocked.Add(ref _categories[(int)cat], len);

                if (len >= KeepBytes)
                {
                    var file = new DiskNode
                    {
                        Name = entry.Name, FullPath = entry.FullName, Size = len, Category = cat,
                        Modified = entry.LastWriteTime, Parent = node,
                    };
                    node.Children.Add(file);
                    AddTop(file);
                }
                else
                {
                    small += len;
                    smallCount++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // pasta ilegível (permissão/erro de disco): segue com o que deu para ler
        }

        var children = new DiskNode?[subdirs.Count];
        void Do(int i)
        {
            var d = subdirs[i];
            var child = new DiskNode { Name = d.Name, FullPath = d.FullName, IsDir = true, Modified = d.LastWriteTime, Parent = node };
            ScanDir(d.FullName, child, depth + 1);
            children[i] = child;
        }

        if (depth <= 1 && subdirs.Count > 1)
            Parallel.For(0, subdirs.Count, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = _ct }, Do);
        else
            for (int i = 0; i < subdirs.Count; i++) Do(i);

        foreach (var child in children)
        {
            if (child is null) continue;
            if (child.Size >= KeepBytes) node.Children.Add(child);
            else
            {
                small += child.Size;
                smallCount++;
            }
        }

        if (small > 0)
            node.Children.Add(new DiskNode { Name = "", IsOther = true, Items = smallCount, Size = small, Parent = node });

        node.Size = node.Children.Sum(c => c.Size);
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        Report();
    }
}
