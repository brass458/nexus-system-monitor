using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NexusMonitor.Core.Models;
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

/// <summary>
/// Maps <see cref="EventClassification"/> to a color brush:
///   HardwareBottleneck → red    (#FF453A)
///   ApplicationSpike   → orange (#FF9F0A)
///   ApplicationLeak    → yellow (#FFD60A)
///   ThermalThrottle    → red-orange (#FF6B35)
///   Unknown            → gray
/// </summary>
public class EventClassificationToBrushConverter : IValueConverter
{
    public static readonly EventClassificationToBrushConverter Instance = new();

    private static readonly SolidColorBrush _hardware = new(Color.Parse("#FF453A"));
    private static readonly SolidColorBrush _spike    = new(Color.Parse("#FF9F0A"));
    private static readonly SolidColorBrush _leak     = new(Color.Parse("#FFD60A"));
    private static readonly SolidColorBrush _thermal  = new(Color.Parse("#FF6B35"));
    private static readonly SolidColorBrush _unknown  = new(Color.Parse("#8E8E93"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is EventClassification c ? c switch
        {
            EventClassification.HardwareBottleneck => _hardware,
            EventClassification.ApplicationSpike   => _spike,
            EventClassification.ApplicationLeak    => _leak,
            EventClassification.ThermalThrottle    => _thermal,
            _                                      => _unknown,
        } : _unknown;

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}

/// <summary>Maps <see cref="EventClassification"/> to a short label.</summary>
public class EventClassificationToLabelConverter : IValueConverter
{
    public static readonly EventClassificationToLabelConverter Instance = new();

    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is EventClassification cl ? cl switch
        {
            EventClassification.HardwareBottleneck => "HW BOTTLENECK",
            EventClassification.ApplicationSpike   => "APP SPIKE",
            EventClassification.ApplicationLeak    => "MEMORY LEAK",
            EventClassification.ThermalThrottle    => "THERMAL",
            _                                      => "UNKNOWN",
        } : "UNKNOWN";

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}

/// <summary>Maps <see cref="ResourceType"/> to a short label.</summary>
public class ResourceTypeToLabelConverter : IValueConverter
{
    public static readonly ResourceTypeToLabelConverter Instance = new();

    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is ResourceType r ? r switch
        {
            ResourceType.Cpu  => "CPU",
            ResourceType.Ram  => "RAM",
            ResourceType.Gpu  => "GPU",
            ResourceType.Vram => "VRAM",
            ResourceType.Disk => "DISK",
            _                 => r.ToString().ToUpperInvariant(),
        } : string.Empty;

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}
