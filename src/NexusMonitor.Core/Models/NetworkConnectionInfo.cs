namespace NexusMonitor.Core.Models;

public enum NetworkProtocol { Tcp, Udp, Tcp6, Udp6 }
public enum TcpState
{
    Closed, Listen, SynSent, SynReceived, Established,
    FinWait1, FinWait2, CloseWait, Closing, LastAck, TimeWait, DeleteTcb, Unknown
}

public record NetworkConnectionInfo
{
    public NetworkProtocol Protocol { get; init; }
    public string LocalAddress { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public string RemoteAddress { get; init; } = string.Empty;
    public int RemotePort { get; init; }
    public TcpState State { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
}
