using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace NexusMonitor.UI.Converters;

/// <summary>Converts a hex color string (e.g., "#5B9BD5") to an Avalonia IBrush.
/// Brushes are cached to avoid per-tick allocation.</summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public static readonly HexColorToBrushConverter Instance = new();

    private static readonly ConcurrentDictionary<string, IImmutableSolidColorBrush> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly IImmutableSolidColorBrush _transparent =
        new ImmutableSolidColorBrush(Colors.Transparent);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            return _cache.GetOrAdd(hex, static key =>
            {
                try { return new ImmutableSolidColorBrush(Color.Parse(key)); }
                catch { return new ImmutableSolidColorBrush(Colors.Transparent); }
            });
        }
        return _transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
