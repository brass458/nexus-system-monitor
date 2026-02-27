using System.Globalization;
using Avalonia.Data.Converters;

namespace NexusMonitor.UI.Converters;

/// <summary>Converts a byte count (long) to gigabytes (double) for display.</summary>
public sealed class BytesToGigabytesConverter : IValueConverter
{
    public static readonly BytesToGigabytesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long   l) return l   / (1024.0 * 1024.0 * 1024.0);
        if (value is ulong ul) return ul  / (1024.0 * 1024.0 * 1024.0);
        if (value is int    i) return i   / (1024.0 * 1024.0 * 1024.0);
        if (value is double d) return d   / (1024.0 * 1024.0 * 1024.0);
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
