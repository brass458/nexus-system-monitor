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
