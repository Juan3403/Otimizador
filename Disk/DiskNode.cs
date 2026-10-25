namespace Otimizador.App.Disk;

/// <summary>Pasta ou arquivo grande da varredura. Itens pequenos de uma pasta ficam agrupados em um único nó "IsOther".</summary>
public sealed class DiskNode
{
    public required string Name { get; init; }
    public string FullPath { get; init; } = "";
    public long Size { get; set; }
    public bool IsDir { get; init; }

    /// <summary>Agrupa os arquivos/pastas pequenos de uma pasta (não é um item real do disco).</summary>
    public bool IsOther { get; init; }

    /// <summary>Quantidade de itens agrupados (só para IsOther).</summary>
    public long Items { get; init; }

    public FileCategory Category { get; init; } = FileCategory.Other;
    public DateTime Modified { get; init; }
    public DiskNode? Parent { get; set; }
    public List<DiskNode> Children { get; } = new();

    /// <summary>Remove este nó da árvore e desconta o tamanho de todas as pastas acima.</summary>
    public void Detach()
    {
        for (var p = Parent; p is not null; p = p.Parent) p.Size -= Size;
        Parent?.Children.Remove(this);
    }

    public bool IsInside(string path) =>
        FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)
        || FullPath.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
