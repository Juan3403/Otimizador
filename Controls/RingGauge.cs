using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Otimizador.App.Controls;

/// <summary>Medidor circular 0–100 com texto central.</summary>
public sealed class RingGauge : FrameworkElement
{
    private static FrameworkPropertyMetadata Render(object? def) =>
        new(def, FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge),
            new FrameworkPropertyMetadata(0.0, OnValueChanged));

    // Valor exibido: anima suavemente até o valor real a cada atualização.
    private static readonly DependencyProperty DisplayValueProperty =
        DependencyProperty.Register(nameof(DisplayValue), typeof(double), typeof(RingGauge), Render(0.0));

    private double DisplayValue => (double)GetValue(DisplayValueProperty);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (RingGauge)d;
        double target = double.IsNaN((double)e.NewValue) ? 0 : (double)e.NewValue;
        gauge.BeginAnimation(DisplayValueProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(RingGauge), Render(""));
    public static readonly DependencyProperty SubTextProperty =
        DependencyProperty.Register(nameof(SubText), typeof(string), typeof(RingGauge), Render(""));
    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RingGauge), Render(Brushes.Gainsboro));
    public static readonly DependencyProperty ValueBrushProperty =
        DependencyProperty.Register(nameof(ValueBrush), typeof(Brush), typeof(RingGauge), Render(Brushes.DodgerBlue));
    public static readonly DependencyProperty TextBrushProperty =
        DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(RingGauge), Render(Brushes.Black));
    public static readonly DependencyProperty SubTextBrushProperty =
        DependencyProperty.Register(nameof(SubTextBrush), typeof(Brush), typeof(RingGauge), Render(Brushes.Gray));
    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(RingGauge), Render(9.0));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string SubText { get => (string)GetValue(SubTextProperty); set => SetValue(SubTextProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush ValueBrush { get => (Brush)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush SubTextBrush { get => (Brush)GetValue(SubTextBrushProperty); set => SetValue(SubTextBrushProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 10) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = size / 2 - Thickness / 2 - 1;

        dc.DrawEllipse(null, new Pen(TrackBrush, Thickness), center, radius, radius);

        double v = double.IsNaN(DisplayValue) ? 0 : Math.Clamp(DisplayValue, 0, 100);
        if (v > 0.2)
        {
            double angle = v / 100 * 359.99;
            double rad = (angle - 90) * Math.PI / 180;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();
            var pen = new Pen(ValueBrush, Thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            dc.DrawGeometry(null, pen, geo);
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var main = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size * 0.24, TextBrush, dpi);
        bool hasSub = !string.IsNullOrEmpty(SubText);
        double mainY = center.Y - main.Height / 2 - (hasSub ? size * 0.07 : 0);
        dc.DrawText(main, new Point(center.X - main.Width / 2, mainY));
        if (hasSub)
        {
            var sub = new FormattedText(SubText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size * 0.105, SubTextBrush, dpi);
            dc.DrawText(sub, new Point(center.X - sub.Width / 2, mainY + main.Height - 1));
        }
    }
}


