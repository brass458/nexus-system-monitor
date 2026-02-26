using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NexusMonitor.Core.Models;

namespace NexusMonitor.UI.Converters;

/// <summary>Converts a ProcessCategory to its foreground color brush.</summary>
public class ProcessCategoryToBrushConverter : IValueConverter
{
    public static readonly ProcessCategoryToBrushConverter Instance = new();

    private static readonly Dictionary<ProcessCategory, string> _brushKeys = new()
    {
        [ProcessCategory.SystemKernel]   = "ProcSystemBrush",
        [ProcessCategory.WindowsService] = "ProcServiceBrush",
        [ProcessCategory.UserApplication]= "ProcUserBrush",
        [ProcessCategory.DotNetManaged]  = "ProcDotNetBrush",
        [ProcessCategory.Suspicious]     = "ProcSuspiciousBrush",
        [ProcessCategory.Suspended]      = "ProcSuspendedBrush",
        [ProcessCategory.GpuAccelerated] = "ProcGpuBrush",
        [ProcessCategory.CurrentProcess] = "ProcCurrentBrush",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ProcessCategory cat && _brushKeys.TryGetValue(cat, out var key))
        {
            if (Application.Current?.Resources.TryGetResource(key, null, out var res) == true)
                return res;
        }
        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>Converts a ProcessCategory to its background (tinted) color brush.</summary>
public class ProcessCategoryToBgBrushConverter : IValueConverter
{
    public static readonly ProcessCategoryToBgBrushConverter Instance = new();

    private static readonly Dictionary<ProcessCategory, string> _brushKeys = new()
    {
        [ProcessCategory.SystemKernel]   = "ProcSystemBgBrush",
        [ProcessCategory.WindowsService] = "ProcServiceBgBrush",
        [ProcessCategory.UserApplication]= "ProcUserBgBrush",
        [ProcessCategory.DotNetManaged]  = "ProcDotNetBgBrush",
        [ProcessCategory.Suspicious]     = "ProcSuspiciousBgBrush",
        [ProcessCategory.Suspended]      = "ProcSuspendedBgBrush",
        [ProcessCategory.GpuAccelerated] = "ProcGpuBgBrush",
        [ProcessCategory.CurrentProcess] = "ProcCurrentBgBrush",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ProcessCategory cat && _brushKeys.TryGetValue(cat, out var key))
        {
            if (Application.Current?.Resources.TryGetResource(key, null, out var res) == true)
                return res;
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>Maps a 0-100 CPU percent to a heat-color brush (green → orange → red).</summary>
public class CpuHeatBrushConverter : IValueConverter
{
    public static readonly CpuHeatBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double pct) return Brushes.Transparent;
        var t = Math.Clamp(pct / 100.0, 0, 1);
        Color color;
        if (t < 0.5)
        {
            // green (#34C759) → orange (#FF9F0A)
            var u = t * 2;
            color = new Color(255,
                (byte)(0x34 + (0xFF - 0x34) * u),
                (byte)(0xC7 + (0x9F - 0xC7) * u),
                (byte)(0x59 + (0x0A - 0x59) * u));
        }
        else
        {
            // orange (#FF9F0A) → red (#FF453A)
            var u = (t - 0.5) * 2;
            color = new Color(255,
                0xFF,
                (byte)(0x9F + (0x45 - 0x9F) * u),
                (byte)(0x0A + (0x3A - 0x0A) * u));
        }
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>Returns true when CPU% > 0.05 (for text visibility).</summary>
public class CpuNonZeroConverter : IValueConverter
{
    public static readonly CpuNonZeroConverter Instance = new();
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
        => value is double d && d > 0.05;
    public object? ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => AvaloniaProperty.UnsetValue;
}

/// <summary>Formats bytes into human-readable string.</summary>
public class BytesFormatConverter : IValueConverter
{
    public static readonly BytesFormatConverter Instance = new();
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is long b) return FormatBytes(b);
        if (value is int i)  return FormatBytes(i);
        return "0 B";
    }
    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => AvaloniaProperty.UnsetValue;

    private static string FormatBytes(long b)
    {
        if (b >= 1_073_741_824) return $"{b / 1_073_741_824.0:F1} GB";
        if (b >= 1_048_576)     return $"{b / 1_048_576.0:F1} MB";
        if (b >= 1_024)         return $"{b / 1_024.0:F0} KB";
        return $"{b} B";
    }
}
