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
}
