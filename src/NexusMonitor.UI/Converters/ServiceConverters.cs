using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using NexusMonitor.Core.Models;

namespace NexusMonitor.UI.Converters;

public class ServiceStateToColorConverter : IValueConverter
{
    public static readonly ServiceStateToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = value is ServiceState state ? state switch
        {
            ServiceState.Running => Color.Parse("#34C759"),
            ServiceState.Stopped => Color.Parse("#636366"),
            ServiceState.Paused  => Color.Parse("#FF9F0A"),
            _                    => Color.Parse("#3A3A3C")
        } : Color.Parse("#3A3A3C");
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}

public class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();
    public object? Convert(object? value, Type t, object? p, CultureInfo c) => value is not null;
    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}

/// <summary>Converts bool IsEnabled → "Enabled" / "Disabled" text.</summary>
public class BoolToEnabledTextConverter : IValueConverter
{
    public static readonly BoolToEnabledTextConverter Instance = new();
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? "Enabled" : "Disabled";
    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}

/// <summary>Converts bool IsEnabled → green (enabled) or gray (disabled) brush.</summary>
public class BoolToEnabledBrushConverter : IValueConverter
{
    public static readonly BoolToEnabledBrushConverter Instance = new();
    private static readonly SolidColorBrush _enabled  = new(Color.Parse("#34C759"));
    private static readonly SolidColorBrush _disabled = new(Color.Parse("#636366"));
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? _enabled : _disabled;
    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}

/// <summary>Maps TcpConnectionState to a background brush for the badge.</summary>
public class TcpStateBrushConverter : IValueConverter
{
    public static readonly TcpStateBrushConverter Instance = new();
    private static readonly SolidColorBrush _green  = new(Color.Parse("#34C759"));
    private static readonly SolidColorBrush _blue   = new(Color.Parse("#0A84FF"));
    private static readonly SolidColorBrush _orange = new(Color.Parse("#FF9F0A"));
    private static readonly SolidColorBrush _gray   = new(Color.Parse("#636366"));

    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is TcpConnectionState state ? state switch
        {
            TcpConnectionState.Established                                      => _green,
            TcpConnectionState.Listen                                           => _blue,
            TcpConnectionState.TimeWait or TcpConnectionState.CloseWait
                or TcpConnectionState.FinWait1 or TcpConnectionState.FinWait2
                or TcpConnectionState.Closing or TcpConnectionState.LastAck    => _orange,
            _                                                                   => _gray,
        } : _gray;

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}

/// <summary>Maps TcpConnectionState to a short display label.</summary>
public class TcpStateLabelConverter : IValueConverter
{
    public static readonly TcpStateLabelConverter Instance = new();

    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is TcpConnectionState state ? state switch
        {
            TcpConnectionState.Established  => "ESTABLISHED",
            TcpConnectionState.Listen       => "LISTEN",
            TcpConnectionState.SynSent      => "SYN_SENT",
            TcpConnectionState.SynReceived  => "SYN_RCVD",
            TcpConnectionState.FinWait1     => "FIN_WAIT1",
            TcpConnectionState.FinWait2     => "FIN_WAIT2",
            TcpConnectionState.CloseWait    => "CLOSE_WAIT",
            TcpConnectionState.Closing      => "CLOSING",
            TcpConnectionState.LastAck      => "LAST_ACK",
            TcpConnectionState.TimeWait     => "TIME_WAIT",
            TcpConnectionState.Closed       => "CLOSED",
            TcpConnectionState.DeleteTcb    => "DELETE_TCB",
            _                               => "—",
        } : "—";

    public object? ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => Avalonia.AvaloniaProperty.UnsetValue;
}
