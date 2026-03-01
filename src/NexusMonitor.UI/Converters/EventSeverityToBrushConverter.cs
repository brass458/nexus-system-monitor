using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NexusMonitor.Core.Storage;

namespace NexusMonitor.UI.Converters;

/// <summary>
/// Maps <see cref="EventSeverity"/> integer values to color brushes:
///   0 (Info)     → blue   (#0A84FF)
///   1 (Warning)  → orange (#FF9F0A)
///   2 (Critical) → red    (#FF453A)
/// </summary>
public class EventSeverityToBrushConverter : IValueConverter
{
    public static readonly EventSeverityToBrushConverter Instance = new();

    private static readonly SolidColorBrush _info     = new(Color.Parse("#0A84FF"));
    private static readonly SolidColorBrush _warning  = new(Color.Parse("#FF9F0A"));
    private static readonly SolidColorBrush _critical = new(Color.Parse("#FF453A"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int severity ? severity switch
        {
            EventSeverity.Warning  => _warning,
            EventSeverity.Critical => _critical,
            _                      => _info,
        } : _info;

    public object? ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}

/// <summary>
/// Maps <see cref="EventSeverity"/> integer to a short label:
///   0 → INFO  ·  1 → WARN  ·  2 → CRIT
/// </summary>
public class EventSeverityToLabelConverter : IValueConverter
{
    public static readonly EventSeverityToLabelConverter Instance = new();

    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is int severity ? severity switch
        {
            EventSeverity.Warning  => "WARN",
            EventSeverity.Critical => "CRIT",
            _                      => "INFO",
        } : "INFO";

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}
