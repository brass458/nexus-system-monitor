using System.Runtime.InteropServices;

namespace NexusMonitor.Platform.Windows.Native;

// ─── GetExtendedTcpTable / GetExtendedUdpTable ────────────────────────────────

internal static partial class IpHelper
{
    private const string Dll = "iphlpapi.dll";

    internal enum TCP_TABLE_CLASS : int
    {
        TCP_TABLE_BASIC_LISTENER          = 0,
        TCP_TABLE_BASIC_CONNECTIONS       = 1,
        TCP_TABLE_BASIC_ALL               = 2,
        TCP_TABLE_OWNER_PID_LISTENER      = 3,
        TCP_TABLE_OWNER_PID_CONNECTIONS   = 4,
        TCP_TABLE_OWNER_PID_ALL           = 5,
        TCP_TABLE_OWNER_MODULE_LISTENER   = 6,
        TCP_TABLE_OWNER_MODULE_CONNECTIONS= 7,
        TCP_TABLE_OWNER_MODULE_ALL        = 8,
    }

    internal enum UDP_TABLE_CLASS : int
    {
        UDP_TABLE_BASIC        = 0,
        UDP_TABLE_OWNER_PID    = 1,
        UDP_TABLE_OWNER_MODULE = 2,
    }

    // Returns ERROR_SUCCESS (0) on success, ERROR_INSUFFICIENT_BUFFER if too small.
    [LibraryImport(Dll)]
    public static partial uint GetExtendedTcpTable(
        nint          pTcpTable,
        ref int       dwOutBufLen,
        int           bOrder,        // BOOL — 1 = sort
        int           ulAf,          // AF_INET = 2, AF_INET6 = 23
        TCP_TABLE_CLASS TableClass,
        uint          Reserved);

    [LibraryImport(Dll)]
    public static partial uint GetExtendedUdpTable(
        nint          pUdpTable,
        ref int       dwOutBufLen,
        int           bOrder,
        int           ulAf,
        UDP_TABLE_CLASS TableClass,
        uint          Reserved);

    // ── TCP EStats — per-connection byte counters ─────────────────────────────

    internal enum TCP_ESTATS_TYPE : int
    {
        TcpConnectionEstatsSynOpts = 0,
        TcpConnectionEstatsData    = 1,
    }

    /// <summary>
    /// Reads per-connection extended statistics. Rod receives cumulative byte counters.
    /// Returns 0 on success. Requires EStats to be enabled first via Set.
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial uint GetPerTcpConnectionEStats(
        ref MIB_TCPROW_FOR_ESTATS Row,
        TCP_ESTATS_TYPE           EstatsType,
        nint Rw,  uint RwVersion,  uint RwSize,
        nint Ros, uint RosVersion, uint RosSize,
        nint Rod, uint RodVersion, uint RodSize);

    /// <summary>
    /// Enables per-connection EStats data collection. Silently fails without admin rights
    /// for connections owned by other processes (which is fine — we just won't get data).
    /// </summary>
    [LibraryImport(Dll)]
    internal static partial uint SetPerTcpConnectionEStats(
        ref MIB_TCPROW_FOR_ESTATS Row,
        TCP_ESTATS_TYPE           EstatsType,
        nint Rw, uint RwVersion, uint RwSize,
        uint Offset);
}

// ─── Row structures ───────────────────────────────────────────────────────────
// All port fields are 32-bit DWORDs; only the low 16 bits are valid, in
// network (big-endian) byte order.  Address fields are also in network byte order.

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_TCPROW_OWNER_PID
{
    public uint dwState;
    public uint dwLocalAddr;
    public uint dwLocalPort;
    public uint dwRemoteAddr;
    public uint dwRemotePort;
    public uint dwOwningPid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_TCP6ROW_OWNER_PID
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] ucLocalAddr;
    public uint dwLocalScopeId;
    public uint dwLocalPort;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] ucRemoteAddr;
    public uint dwRemoteScopeId;
    public uint dwRemotePort;
    public uint dwState;
    public uint dwOwningPid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_UDPROW_OWNER_PID
{
    public uint dwLocalAddr;
    public uint dwLocalPort;
    public uint dwOwningPid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_UDP6ROW_OWNER_PID
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] ucLocalAddr;
    public uint dwLocalScopeId;
    public uint dwLocalPort;
    public uint dwOwningPid;
}

// ─── EStats structures ────────────────────────────────────────────────────────

/// <summary>
/// MIB_TCPROW without the owning-PID field, as required by GetPerTcpConnectionEStats.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MIB_TCPROW_FOR_ESTATS
{
    public uint dwState;
    public uint dwLocalAddr;
    public uint dwLocalPort;
    public uint dwRemoteAddr;
    public uint dwRemotePort;
}

/// <summary>
/// Read/write settings struct — BOOLEAN(1 byte) that enables data collection.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TCP_ESTATS_DATA_RW_v0
{
    public byte EnableCollection;  // BOOLEAN
}

/// <summary>
/// Read-only data struct returned by GetPerTcpConnectionEStats.
/// DataBytesOut / DataBytesIn are cumulative byte counts since the connection was established.
/// Full size: 80 bytes (matches Windows SDK TCP_ESTATS_DATA_ROD_v0 with natural alignment).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TCP_ESTATS_DATA_ROD_v0
{
    public ulong DataBytesOut;      // offset  0 — cumulative bytes sent
    public ulong DataBytesIn;       // offset  8 — cumulative bytes received
    public ulong SegsOut;           // offset 16
    public ulong SegsIn;            // offset 24
    public uint  SoftErrors;        // offset 32
    public uint  SoftErrorReason;   // offset 36
    public uint  SndUna;            // offset 40
    public uint  SndNxt;            // offset 44
    public uint  SndMax;            // offset 48
    // 4 bytes natural padding to align next ulong to offset 56
    private uint _pad1;
    public ulong ThruBytesAcked;    // offset 56
    public uint  RcvNxt;            // offset 64
    // 4 bytes natural padding to align next ulong to offset 72
    private uint _pad2;
    public ulong ThruBytesReceived; // offset 72  → struct ends at 80
}
