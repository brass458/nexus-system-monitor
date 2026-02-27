namespace NexusMonitor.Core.Models;

public enum ConnectionProtocol { Tcp4, Tcp6, Udp4, Udp6 }

public enum TcpConnectionState
{
    Closed = 1, Listen, SynSent, SynReceived, Established,
    FinWait1, FinWait2, CloseWait, Closing, LastAck, TimeWait, DeleteTcb, Unknown
}

public record NetworkConnection
{
    public ConnectionProtocol Protocol  { get; init; }
    public string LocalAddress          { get; init; } = string.Empty;
    public int    LocalPort             { get; init; }
    public string RemoteAddress         { get; init; } = string.Empty;
    public int    RemotePort            { get; init; }
    public TcpConnectionState State     { get; init; }
    public int    ProcessId             { get; init; }
    public string ProcessName           { get; init; } = string.Empty;

    // Per-connection throughput (populated by platform provider; 0 when unavailable)
    public long SendBytesPerSec         { get; init; }
    public long RecvBytesPerSec         { get; init; }

    public string SendDisplay => SendBytesPerSec > 0 ? FormatRate(SendBytesPerSec) : "\u2014";
    public string RecvDisplay => RecvBytesPerSec > 0 ? FormatRate(RecvBytesPerSec) : "\u2014";

    private static string FormatRate(long bps) => bps switch
    {
        >= 1_048_576 => $"{bps / 1_048_576.0:F1} MB/s",
        >= 1_024     => $"{bps / 1_024.0:F0} KB/s",
        _            => $"{bps} B/s",
    };
}
