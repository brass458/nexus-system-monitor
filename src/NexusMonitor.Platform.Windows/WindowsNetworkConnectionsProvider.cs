using System.Diagnostics;
using System.Net;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Platform.Windows.Native;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Enumerates active TCP and UDP connections via IPHelper
/// GetExtendedTcpTable / GetExtendedUdpTable.
/// </summary>
public sealed class WindowsNetworkConnectionsProvider : INetworkConnectionsProvider
{
    private const int AF_INET  = 2;
    private const int AF_INET6 = 23;

    public IObservable<IReadOnlyList<NetworkConnection>> GetConnectionStream(TimeSpan interval) =>
        Observable.Timer(TimeSpan.Zero, interval)
                  .Select(_ => (IReadOnlyList<NetworkConnection>)Snapshot());

    public Task<IReadOnlyList<NetworkConnection>> GetConnectionsAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<NetworkConnection>>(() => Snapshot(), ct);

    // ─── Snapshot ─────────────────────────────────────────────────────────────

    private static List<NetworkConnection> Snapshot()
    {
        var names  = GetProcessNames();
        var result = new List<NetworkConnection>(256);

        try { result.AddRange(GetTcp4(names)); } catch { }
        try { result.AddRange(GetTcp6(names)); } catch { }
        try { result.AddRange(GetUdp4(names)); } catch { }
        try { result.AddRange(GetUdp6(names)); } catch { }

        return result;
    }

    // ─── TCP IPv4 ─────────────────────────────────────────────────────────────

    private static IEnumerable<NetworkConnection> GetTcp4(Dictionary<int, string> names)
    {
        int size = 0;
        IpHelper.GetExtendedTcpTable(nint.Zero, ref size, 0, AF_INET,
            IpHelper.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) yield break;

        nint buf = Marshal.AllocHGlobal(size);
        try
        {
            if (IpHelper.GetExtendedTcpTable(buf, ref size, 1, AF_INET,
                    IpHelper.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                yield break;

            int count   = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(buf + 4 + i * rowSize);
                int pid = (int)row.dwOwningPid;
                yield return new NetworkConnection
                {
                    Protocol      = ConnectionProtocol.Tcp4,
                    LocalAddress  = Addr4(row.dwLocalAddr),
                    LocalPort     = Port(row.dwLocalPort),
                    RemoteAddress = Addr4(row.dwRemoteAddr),
                    RemotePort    = Port(row.dwRemotePort),
                    State         = TcpState(row.dwState),
                    ProcessId     = pid,
                    ProcessName   = names.GetValueOrDefault(pid, "—"),
                };
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ─── TCP IPv6 ─────────────────────────────────────────────────────────────

    private static IEnumerable<NetworkConnection> GetTcp6(Dictionary<int, string> names)
    {
        int size = 0;
        IpHelper.GetExtendedTcpTable(nint.Zero, ref size, 0, AF_INET6,
            IpHelper.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) yield break;

        nint buf = Marshal.AllocHGlobal(size);
        try
        {
            if (IpHelper.GetExtendedTcpTable(buf, ref size, 1, AF_INET6,
                    IpHelper.TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                yield break;

            int count   = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(buf + 4 + i * rowSize);
                int pid = (int)row.dwOwningPid;
                yield return new NetworkConnection
                {
                    Protocol      = ConnectionProtocol.Tcp6,
                    LocalAddress  = Addr6(row.ucLocalAddr),
                    LocalPort     = Port(row.dwLocalPort),
                    RemoteAddress = Addr6(row.ucRemoteAddr),
                    RemotePort    = Port(row.dwRemotePort),
                    State         = TcpState(row.dwState),
                    ProcessId     = pid,
                    ProcessName   = names.GetValueOrDefault(pid, "—"),
                };
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ─── UDP IPv4 ─────────────────────────────────────────────────────────────

    private static IEnumerable<NetworkConnection> GetUdp4(Dictionary<int, string> names)
    {
        int size = 0;
        IpHelper.GetExtendedUdpTable(nint.Zero, ref size, 0, AF_INET,
            IpHelper.UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
        if (size == 0) yield break;

        nint buf = Marshal.AllocHGlobal(size);
        try
        {
            if (IpHelper.GetExtendedUdpTable(buf, ref size, 1, AF_INET,
                    IpHelper.UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0) != 0)
                yield break;

            int count   = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(buf + 4 + i * rowSize);
                int pid = (int)row.dwOwningPid;
                yield return new NetworkConnection
                {
                    Protocol      = ConnectionProtocol.Udp4,
                    LocalAddress  = Addr4(row.dwLocalAddr),
                    LocalPort     = Port(row.dwLocalPort),
                    RemoteAddress = "—",
                    RemotePort    = 0,
                    State         = TcpConnectionState.Unknown,
                    ProcessId     = pid,
                    ProcessName   = names.GetValueOrDefault(pid, "—"),
                };
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ─── UDP IPv6 ─────────────────────────────────────────────────────────────

    private static IEnumerable<NetworkConnection> GetUdp6(Dictionary<int, string> names)
    {
        int size = 0;
        IpHelper.GetExtendedUdpTable(nint.Zero, ref size, 0, AF_INET6,
            IpHelper.UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
        if (size == 0) yield break;

        nint buf = Marshal.AllocHGlobal(size);
        try
        {
            if (IpHelper.GetExtendedUdpTable(buf, ref size, 1, AF_INET6,
                    IpHelper.UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0) != 0)
                yield break;

            int count   = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(buf + 4 + i * rowSize);
                int pid = (int)row.dwOwningPid;
                yield return new NetworkConnection
                {
                    Protocol      = ConnectionProtocol.Udp6,
                    LocalAddress  = Addr6(row.ucLocalAddr),
                    LocalPort     = Port(row.dwLocalPort),
                    RemoteAddress = "—",
                    RemotePort    = 0,
                    State         = TcpConnectionState.Unknown,
                    ProcessId     = pid,
                    ProcessName   = names.GetValueOrDefault(pid, "—"),
                };
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Dictionary<int, string> GetProcessNames()
    {
        try
        {
            var procs = Process.GetProcesses();
            var dict  = new Dictionary<int, string>(procs.Length);
            foreach (var p in procs)
            {
                try { dict[p.Id] = p.ProcessName; } catch { }
                p.Dispose();
            }
            return dict;
        }
        catch { return []; }
    }

    // Port: DWORD stores a 16-bit value in network (big-endian) byte order.
    private static int Port(uint p) =>
        (IPAddress.NetworkToHostOrder((short)(p & 0xFFFF)) & 0xFFFF);

    // IPv4 address: DWORD in network byte order — BitConverter gives bytes in
    // memory order which IPAddress(byte[]) expects in network order. ✓
    private static string Addr4(uint a) =>
        new IPAddress(BitConverter.GetBytes(a)).ToString();

    private static string Addr6(byte[]? a) =>
        a is null ? "::" : new IPAddress(a).ToString();

    private static TcpConnectionState TcpState(uint s) => s switch
    {
        1  => TcpConnectionState.Closed,
        2  => TcpConnectionState.Listen,
        3  => TcpConnectionState.SynSent,
        4  => TcpConnectionState.SynReceived,
        5  => TcpConnectionState.Established,
        6  => TcpConnectionState.FinWait1,
        7  => TcpConnectionState.FinWait2,
        8  => TcpConnectionState.CloseWait,
        9  => TcpConnectionState.Closing,
        10 => TcpConnectionState.LastAck,
        11 => TcpConnectionState.TimeWait,
        12 => TcpConnectionState.DeleteTcb,
        _  => TcpConnectionState.Unknown,
    };
}
