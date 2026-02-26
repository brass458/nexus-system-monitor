using System.Runtime.InteropServices;

namespace NexusMonitor.Platform.Windows.Native;

internal static partial class AdvApi32
{
    private const string Dll = "advapi32.dll";

    // ─── Token information ────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a SID to a domain\account string.
    /// Uses raw nint buffers (LibraryImport doesn't support StringBuilder).
    /// </summary>
    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupAccountSidW(
        nint    lpSystemName,           // null = local machine
        nint    Sid,
        nint    Name,                   // caller-allocated WCHAR buffer
        ref int cchName,
        nint    ReferencedDomainName,   // caller-allocated WCHAR buffer
        ref int cchReferencedDomainName,
        out uint peUse);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(
        nint TokenHandle,
        TOKEN_INFORMATION_CLASS TokenInformationClass,
        nint TokenInformation,
        uint TokenInformationLength,
        out uint ReturnLength);

    // ─── Service Control Manager ──────────────────────────────────────────────

    public const uint SC_MANAGER_CONNECT          = 0x0001;
    public const uint SC_MANAGER_ENUMERATE_SERVICE= 0x0004;
    public const uint SC_MANAGER_ALL_ACCESS       = 0xF003F;

    public const uint SERVICE_WIN32               = 0x00000030;
    public const uint SERVICE_DRIVER              = 0x0000000B;
    public const uint SERVICE_TYPE_ALL            = 0x0000013F;
    public const uint SERVICE_STATE_ALL           = 0x00000003;

    public const uint SERVICE_QUERY_CONFIG        = 0x0001;
    public const uint SERVICE_CHANGE_CONFIG       = 0x0002;
    public const uint SERVICE_QUERY_STATUS        = 0x0004;
    public const uint SERVICE_START               = 0x0010;
    public const uint SERVICE_STOP                = 0x0020;
    public const uint SERVICE_ALL_ACCESS          = 0xF01FF;

    public const uint SC_ENUM_PROCESS_INFO        = 0;

    public const uint SERVICE_CONFIG_DESCRIPTION             = 1;
    public const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    public const uint SERVICE_CONTROL_STOP        = 1;
    public const uint SERVICE_CONTROL_PAUSE       = 2;
    public const uint SERVICE_CONTROL_CONTINUE    = 3;

    public const uint SERVICE_NO_CHANGE           = 0xFFFFFFFF;

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenSCManagerW(
        string? lpMachineName,
        string? lpDatabaseName,
        uint dwDesiredAccess);

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenServiceW(
        nint hSCManager,
        string lpServiceName,
        uint dwDesiredAccess);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseServiceHandle(nint hSCObject);

    /// <summary>
    /// Enumerates services. Pass <see cref="nint.Zero"/> and 0 for buffer/size on first call
    /// to retrieve the required buffer size via <paramref name="pcbBytesNeeded"/>.
    /// </summary>
    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumServicesStatusExW(
        nint hSCManager,
        uint InfoLevel,
        uint dwServiceType,
        uint dwServiceState,
        nint lpServices,
        uint cbBufSize,
        out uint pcbBytesNeeded,
        out uint lpServicesReturned,
        ref uint lpResumeHandle,
        nint pszGroupName);   // null = all groups

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryServiceConfigW(
        nint hService,
        nint lpServiceConfig,
        uint cbBufSize,
        out uint pcbBytesNeeded);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryServiceConfig2W(
        nint hService,
        uint dwInfoLevel,
        nint lpBuffer,
        uint cbBufSize,
        out uint pcbBytesNeeded);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool StartServiceW(
        nint hService,
        uint dwNumServiceArgs,
        nint lpServiceArgVectors);   // null when no args

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ControlService(
        nint hService,
        uint dwControl,
        ref SERVICE_STATUS lpServiceStatus);

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ChangeServiceConfigW(
        nint hService,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string? lpBinaryPathName,
        string? lpLoadOrderGroup,
        nint lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword,
        string? lpDisplayName);
}
