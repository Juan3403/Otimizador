using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Otimizador.App.Controls;

/// <summary>Gráfico de linha leve (histórico recente). NaN em Values = lacuna.</summary>
public sealed class Sparkline : FrameworkElement
{
    private static FrameworkPropertyMetadata Render(object? def) =>
        new(def, FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline), Render(null));
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline), Render(Brushes.DodgerBlue));
    public static readonly DependencyProperty GridBrushProperty =
        DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(Sparkline), Render(Brushes.Gainsboro));
    public static readonly DependencyProperty LabelBrushProperty =
        DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(Sparkline), Render(Brushes.Gray));
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(Sparkline), Render(0.0));
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(Sparkline), Render(100.0));
    public static readonly DependencyProperty CapacityProperty =
        DependencyProperty.Register(nameof(Capacity), typeof(int), typeof(Sparkline), Render(120));
    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(Sparkline), Render(2.0));
    public static readonly DependencyProperty FillAreaProperty =
        DependencyProperty.Register(nameof(FillArea), typeof(bool), typeof(Sparkline), Render(true));
    public static readonly DependencyProperty ShowGridProperty =
        DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(Sparkline), Render(false));
    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(Sparkline), Render(false));
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(Sparkline), Render(""));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public bool FillArea { get => (bool)GetValue(FillAreaProperty); set => SetValue(FillAreaProperty, value); }
    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public bool ShowLabels { get => (bool)GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        double left = ShowLabels ? 34 : 0;
        double plotW = w - left;
        double range = Math.Max(1e-6, Maximum - Minimum);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (ShowGrid)
        {
            var gridPen = new Pen(GridBrush, 1);
            for (int i = 0; i <= 4; i++)
            {
                double y = Math.Round(h * i / 4) + 0.5;
                dc.DrawLine(gridPen, new Point(left, y), new Point(w, y));
                if (ShowLabels)
                {
                    double v = Maximum - range * i / 4;
                    var ft = new FormattedText($"{v:0}{Unit}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10.5, LabelBrush, dpi);
                    dc.DrawText(ft, new Point(0, Math.Clamp(y - ft.Height / 2, 0, h - ft.Height)));
                }
            }
        }

        var values = Values;
        if (values is null || values.Count < 1) return;

        int cap = Math.Max(2, Capacity);
        double step = plotW / (cap - 1);
        int n = values.Count;
        double X(int i) => w - (n - 1 - i) * step;
        double Y(double v) => h - (Math.Clamp(v, Minimum, Maximum) - Minimum) / range * (h - 4) - 2;

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        var dots = new List<Point>();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            int i = 0;
            while (i < n)
            {
                if (double.IsNaN(values[i]))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < n && !double.IsNaN(values[i])) i++;
                int count = i - start;

                if (count == 1)
                {
                    // ponto isolado (ex.: poucos minutos de histórico): vira uma marca
                    dots.Add(new Point(X(start), Y(values[start])));
                    continue;
                }

                var first = new Point(X(start), Y(values[start]));
                lc.BeginFigure(first, false, false);
                ac.BeginFigure(new Point(first.X, h), true, true);
                ac.LineTo(first, false, false);
                var last = first;
                for (int k = start + 1; k < i; k++)
                {
                    last = new Point(X(k), Y(values[k]));
                    lc.LineTo(last, true, true);
                    ac.LineTo(last, false, false);
                }
                ac.LineTo(new Point(last.X, h), false, false);
            }
        }
        line.Freeze();
        area.Freeze();

        if (FillArea && Stroke is SolidColorBrush sb)
        {
            var c = sb.Color;
            var fill = new LinearGradientBrush(
                Color.FromArgb(70, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90);
            fill.Freeze();
            dc.DrawGeometry(fill, null, area);
        }

        var pen = new Pen(Stroke, Thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        dc.DrawGeometry(null, pen, line);
        foreach (var d in dots) dc.DrawRectangle(Stroke, null, new Rect(d.X - 2, d.Y - 2, 4, 4));
    }
}


