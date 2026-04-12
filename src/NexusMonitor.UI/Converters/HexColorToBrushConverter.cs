using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NexusMonitor.UI.Converters;

/// <summary>Converts a hex color string (e.g., "#5B9BD5") to an Avalonia IBrush.</summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { }
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
