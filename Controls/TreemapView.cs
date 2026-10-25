using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Otimizador.App.Disk;

namespace Otimizador.App.Controls;

/// <summary>
/// Mapa de espaço em disco (treemap): cada retângulo é uma pasta ou arquivo, com área proporcional ao tamanho
/// e cor pelo tipo. Clique numa pasta para entrar, botão direito para voltar.
/// </summary>
public sealed class TreemapView : FrameworkElement
{
    private sealed record Cell(DiskNode Node, Rect Rect, bool Container);

    private const int MaxDepth = 6;
    private const double HeaderHeight = 16;

    private static FrameworkPropertyMetadata Render(object? def) =>
        new(def, FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty RootNodeProperty =
        DependencyProperty.Register(nameof(RootNode), typeof(DiskNode), typeof(TreemapView), Render(null));
    public static readonly DependencyProperty VersionProperty =
        DependencyProperty.Register(nameof(Version), typeof(int), typeof(TreemapView), Render(0));
    public static readonly DependencyProperty BackdropBrushProperty =
        DependencyProperty.Register(nameof(BackdropBrush), typeof(Brush), typeof(TreemapView), Render(Brushes.Black));
    public static readonly DependencyProperty FolderBrushProperty =
        DependencyProperty.Register(nameof(FolderBrush), typeof(Brush), typeof(TreemapView), Render(Brushes.DimGray));
    public static readonly DependencyProperty FolderBorderBrushProperty =
        DependencyProperty.Register(nameof(FolderBorderBrush), typeof(Brush), typeof(TreemapView), Render(Brushes.Gray));
    public static readonly DependencyProperty TextBrushProperty =
        DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(TreemapView), Render(Brushes.White));
    public static readonly DependencyProperty HoverTextProperty =
        DependencyProperty.Register(nameof(HoverText), typeof(string), typeof(TreemapView),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty ActivateCommandProperty =
        DependencyProperty.Register(nameof(ActivateCommand), typeof(ICommand), typeof(TreemapView));
    public static readonly DependencyProperty SelectCommandProperty =
        DependencyProperty.Register(nameof(SelectCommand), typeof(ICommand), typeof(TreemapView));
    public static readonly DependencyProperty UpCommandProperty =
        DependencyProperty.Register(nameof(UpCommand), typeof(ICommand), typeof(TreemapView));

    public DiskNode? RootNode { get => (DiskNode?)GetValue(RootNodeProperty); set => SetValue(RootNodeProperty, value); }
    public int Version { get => (int)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public Brush BackdropBrush { get => (Brush)GetValue(BackdropBrushProperty); set => SetValue(BackdropBrushProperty, value); }
    public Brush FolderBrush { get => (Brush)GetValue(FolderBrushProperty); set => SetValue(FolderBrushProperty, value); }
    public Brush FolderBorderBrush { get => (Brush)GetValue(FolderBorderBrushProperty); set => SetValue(FolderBorderBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public string HoverText { get => (string)GetValue(HoverTextProperty); set => SetValue(HoverTextProperty, value); }
    public ICommand? ActivateCommand { get => (ICommand?)GetValue(ActivateCommandProperty); set => SetValue(ActivateCommandProperty, value); }
    public ICommand? SelectCommand { get => (ICommand?)GetValue(SelectCommandProperty); set => SetValue(SelectCommandProperty, value); }
    public ICommand? UpCommand { get => (ICommand?)GetValue(UpCommandProperty); set => SetValue(UpCommandProperty, value); }

    private static readonly Brush DirLeafBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)));
    private static readonly Brush OtherBrush = Freeze(new SolidColorBrush(Color.FromArgb(70, 0x94, 0xA3, 0xB8)));
    private static readonly Brush LabelShadow = Freeze(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)));

    private readonly List<Cell> _cells = new();
    private DiskNode? _layoutRoot;
    private int _layoutVersion = -1;
    private Size _layoutSize;
    private Cell? _hover;

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    private void EnsureLayout()
    {
        var size = new Size(ActualWidth, ActualHeight);
        if (ReferenceEquals(_layoutRoot, RootNode) && _layoutVersion == Version && _layoutSize == size) return;
        _layoutRoot = RootNode;
        _layoutVersion = Version;
        _layoutSize = size;
        _cells.Clear();
        _hover = null;
        if (RootNode is not null && size.Width > 8 && size.Height > 8)
            Build(RootNode, new Rect(0, 0, size.Width, size.Height), 0);
    }

    private void Build(DiskNode parent, Rect area, int depth)
    {
        var placed = new List<(DiskNode Node, Rect Rect)>();
        TreemapLayout.Layout(parent.Children, area, placed);
        foreach (var (node, rect) in placed)
        {
            if (rect.Width < 1.5 || rect.Height < 1.5) continue;
            bool container = node.IsDir && !node.IsOther && node.Children.Count > 0
                             && depth < MaxDepth && rect.Width > 72 && rect.Height > 50;
            _cells.Add(new Cell(node, rect, container));
            if (container)
                Build(node, new Rect(rect.X + 2, rect.Y + HeaderHeight, rect.Width - 4, rect.Height - HeaderHeight - 2), depth + 1);
        }
    }

    private FormattedText Text(string s, double size, Brush brush, double maxWidth, bool bold = false)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal,
                bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        return ft;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(BackdropBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        EnsureLayout();
        if (_cells.Count == 0) return;

        var gap = new Pen(BackdropBrush, 1);
        var border = new Pen(FolderBorderBrush, 1);
        int labels = 0;

        foreach (var cell in _cells)
        {
            var r = cell.Rect;
            var node = cell.Node;
            if (cell.Container)
            {
                dc.DrawRectangle(FolderBrush, border, r);
                if (r.Width > 54 && labels < 500)
                {
                    labels++;
                    dc.DrawText(Text(node.Name, 11, TextBrush, r.Width - 8, bold: true), new Point(r.X + 4, r.Y + 1));
                }
                continue;
            }

            Brush fill = node.IsOther ? OtherBrush : node.IsDir ? DirLeafBrush : FileCategories.BrushOf(node.Category);
            dc.DrawRectangle(fill, gap, r);

            if (!node.IsOther && r.Width > 62 && r.Height > 26 && labels < 500)
            {
                labels++;
                var name = Text(node.Name, 11, Brushes.White, r.Width - 8);
                dc.DrawText(name, new Point(r.X + 5, r.Y + 3));
                if (r.Height > 42)
                    dc.DrawText(Text(SizeFormat.Bytes(node.Size), 10.5, Brushes.White, r.Width - 8), new Point(r.X + 5, r.Y + 18));
            }
        }

        if (_hover is { } h)
        {
            var outline = new Pen(Brushes.White, 2);
            dc.DrawRectangle(null, new Pen(Brushes.Black, 3.5), Rect.Inflate(h.Rect, -1, -1));
            dc.DrawRectangle(null, outline, Rect.Inflate(h.Rect, -1, -1));
        }
    }

    private Cell? HitTest(Point p)
    {
        // As células aparecem em ordem de desenho (pai antes dos filhos): a última que contém o ponto é a mais interna.
        for (int i = _cells.Count - 1; i >= 0; i--)
            if (_cells[i].Rect.Contains(p))
                return _cells[i];
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cell = HitTest(e.GetPosition(this));
        if (ReferenceEquals(cell, _hover)) return;
        _hover = cell;
        Cursor = cell is { Node: { IsDir: true, IsOther: false } } ? Cursors.Hand : Cursors.Arrow;
        HoverText = cell is null ? "" : Describe(cell.Node);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is null) return;
        _hover = null;
        HoverText = "";
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (HitTest(e.GetPosition(this)) is not { } cell || cell.Node.IsOther) return;
        var cmd = cell.Node.IsDir ? ActivateCommand : SelectCommand;
        if (cmd?.CanExecute(cell.Node) == true) cmd.Execute(cell.Node);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (UpCommand?.CanExecute(null) == true) UpCommand.Execute(null);
    }

    private string Describe(DiskNode n)
    {
        if (n.IsOther) return $"{Loc.T("Space_Small")}: {n.Items:N0}  ·  {SizeFormat.Bytes(n.Size)}";
        double pct = RootNode is { Size: > 0 } r ? n.Size * 100.0 / r.Size : 0;
        return $"{n.FullPath}  ·  {SizeFormat.Bytes(n.Size)}  ·  {pct:0.0}%";
    }
}



