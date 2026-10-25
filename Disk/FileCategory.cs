using System.Windows.Media;

namespace Otimizador.App.Disk;

public enum FileCategory { Video, Image, Audio, Document, Archive, App, Other }

/// <summary>Classifica arquivos por extensão e define a cor de cada tipo no mapa de espaço.</summary>
public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Map = Build();

    private static Dictionary<string, FileCategory> Build()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, string exts)
        {
            foreach (var e in exts.Split(' ', StringSplitOptions.RemoveEmptyEntries)) map["." + e] = c;
        }
        Add(FileCategory.Video, "mp4 mkv avi mov wmv flv webm m4v mpg mpeg ts m2ts vob");
        Add(FileCategory.Image, "jpg jpeg png gif bmp tif tiff webp heic raw cr2 nef arw dng psd ico svg xcf");
        Add(FileCategory.Audio, "mp3 wav flac aac ogg wma m4a opus aiff");
        Add(FileCategory.Document, "pdf doc docx xls xlsx ppt pptx txt rtf csv odt ods md epub mobi");
        Add(FileCategory.Archive, "zip rar 7z tar gz bz2 xz iso img vhd vhdx vmdk wim cab bak dmp");
        Add(FileCategory.App, "exe dll msi sys msu appx msix msixbundle pak jar apk");
        return map;
    }

    public static FileCategory Of(string fileName) =>
        Map.TryGetValue(Path.GetExtension(fileName), out var c) ? c : FileCategory.Other;

    public static Color ColorOf(FileCategory c) => c switch
    {
        FileCategory.Video => Color.FromRgb(0x8B, 0x5C, 0xF6),
        FileCategory.Image => Color.FromRgb(0x14, 0xB8, 0xA6),
        FileCategory.Audio => Color.FromRgb(0xEC, 0x48, 0x99),
        FileCategory.Document => Color.FromRgb(0x3B, 0x82, 0xF6),
        FileCategory.Archive => Color.FromRgb(0xF5, 0x9E, 0x0B),
        FileCategory.App => Color.FromRgb(0x22, 0xC5, 0x5E),
        _ => Color.FromRgb(0x94, 0xA3, 0xB8),
    };

    private static readonly Dictionary<FileCategory, Brush> Brushes = Enum.GetValues<FileCategory>()
        .ToDictionary(c => c, c =>
        {
            var b = new SolidColorBrush(ColorOf(c));
            b.Freeze();
            return (Brush)b;
        });

    public static Brush BrushOf(FileCategory c) => Brushes[c];

    public static string LocKey(FileCategory c) => "Cat_" + c;
}

public static class SizeFormat
{
    public static string Bytes(long b)
    {
        double v = b;
        if (v >= 1L << 40) return $"{v / (1L << 40):0.0} TB";
        if (v >= 1L << 30) return $"{v / (1L << 30):0.0} GB";
        if (v >= 1L << 20) return $"{v / (1L << 20):0.#} MB";
        if (v >= 1L << 10) return $"{v / (1L << 10):0} KB";
        return $"{b} B";
    }
}
