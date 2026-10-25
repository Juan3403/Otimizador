using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Otimizador.App.Converters;

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverte um bool (para IsEnabled = não está ocupado).</summary>
public sealed class NotBool : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Texto vazio/nulo → Collapsed.</summary>
public sealed class StringToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Temperatura (°C) → cor de estado: verde, âmbar ou vermelho.</summary>
public sealed class TempBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value is double t && !double.IsNaN(t)
            ? t >= 85 ? "Danger" : t >= 70 ? "Warning" : "Success"
            : "TextTertiary";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

