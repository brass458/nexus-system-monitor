using System.Runtime.InteropServices;

namespace NexusMonitor.Platform.Windows.Native;

// ─── Process Enumeration ──────────────────────────────────────────────────────
// Uses unsafe fixed char so the struct is blittable — required for LibraryImport.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PROCESSENTRY32
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ProcessID;
    public nint th32DefaultHeapID;
    public uint th32ModuleID;
    public uint cntThreads;
    public uint th32ParentProcessID;
    public int  pcPriClassBase;
    public uint dwFlags;
    private fixed char _szExeFile[260];

    public readonly string ExeFile
    {
        get { fixed (char* p = _szExeFile) return new string(p).TrimEnd('\0'); }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MODULEENTRY32
{
    public uint  dwSize;
    public uint  th32ModuleID;
    public uint  th32ProcessID;
    public uint  GlblcntUsage;
    public uint  ProccntUsage;
    public nint  modBaseAddr;
    public uint  modBaseSize;
    public nint  hModule;
    private fixed char _szModule[256];
    private fixed char _szExePath[260];

    public readonly string Module  { get { fixed (char* p = _szModule)  return new string(p).TrimEnd('\0'); } }
    public readonly string ExePath { get { fixed (char* p = _szExePath) return new string(p).TrimEnd('\0'); } }
}

// ─── NtQueryInformationProcess — ProcessBasicInformation ─────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_BASIC_INFORMATION
{
    public nint ExitStatus;
    public nint PebBaseAddress;
    public nint AffinityMask;
    public nint BasePriority;
    public nint UniqueProcessId;
    public nint InheritedFromUniqueProcessId;   // Parent PID
}

// ─── Memory ───────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_MEMORY_COUNTERS_EX
{
    public uint  cb;
    public uint  PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
    public nuint PrivateUsage;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint  dwLength;
    public uint  dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}

// ─── I/O Counters ─────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct IO_COUNTERS
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}

// ─── Token / Elevation ────────────────────────────────────────────────────────

internal enum TOKEN_INFORMATION_CLASS { TokenUser = 1, TokenElevation = 20, TokenElevationType = 18 }
internal enum TOKEN_ELEVATION_TYPE    { Default = 1, Full = 2, Limited = 3 }

[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_ELEVATION { public uint TokenIsElevated; }

// ─── System Information ───────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_BASIC_INFORMATION
{
    public uint Reserved;
    public uint TimerResolution;
    public uint PageSize;
    public uint NumberOfPhysicalPages;
    public uint LowestPhysicalPageNumber;
    public uint HighestPhysicalPageNumber;
    public uint AllocationGranularity;
    public nint MinimumUserModeAddress;
    public nint MaximumUserModeAddress;
    public nint ActiveProcessorsAffinityMask;
    public byte NumberOfProcessors;
}

// ─── Service Control Manager ──────────────────────────────────────────────────
// Pointer fields are nint so we can read them via Marshal.PtrToStringUni after
// calling Marshal.PtrToStructure from an unmanaged buffer.

[StructLayout(LayoutKind.Sequential)]
internal struct ENUM_SERVICE_STATUS_PROCESS
{
    public nint lpServiceName;      // LPWSTR
    public nint lpDisplayName;      // LPWSTR
    public SERVICE_STATUS_PROCESS ServiceStatusProcess;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SERVICE_STATUS_PROCESS
{
    public uint dwServiceType;
    public uint dwCurrentState;
    public uint dwControlsAccepted;
    public uint dwWin32ExitCode;
    public uint dwServiceSpecificExitCode;
    public uint dwCheckPoint;
    public uint dwWaitHint;
    public uint dwProcessId;
    public uint dwServiceFlags;
}

/// <summary>Basic SERVICE_STATUS used with ControlService.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SERVICE_STATUS
{
    public uint dwServiceType;
    public uint dwCurrentState;
    public uint dwControlsAccepted;
    public uint dwWin32ExitCode;
    public uint dwServiceSpecificExitCode;
    public uint dwCheckPoint;
    public uint dwWaitHint;
}

[StructLayout(LayoutKind.Sequential)]
internal struct QUERY_SERVICE_CONFIG
{
    public uint dwServiceType;
    public uint dwStartType;
    public uint dwErrorControl;
    public nint lpBinaryPathName;   // LPWSTR
    public nint lpLoadOrderGroup;   // LPWSTR
    public uint dwTagId;
    public nint lpDependencies;     // LPWSTR multi-string
    public nint lpServiceStartName; // LPWSTR
    public nint lpDisplayName;      // LPWSTR
}

// ─── NtQueryInformationProcess — ProcessCommandLine ──────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct UNICODE_STRING
{
    public ushort Length;
    public ushort MaximumLength;
    public nint   Buffer;          // Points into the caller's own allocation
}

// ─── SCM State / StartType constants ─────────────────────────────────────────

internal static class NativeServiceState
{
    public const uint SERVICE_STOPPED          = 1;
    public const uint SERVICE_START_PENDING    = 2;
    public const uint SERVICE_STOP_PENDING     = 3;
    public const uint SERVICE_RUNNING          = 4;
    public const uint SERVICE_CONTINUE_PENDING = 5;
    public const uint SERVICE_PAUSE_PENDING    = 6;
    public const uint SERVICE_PAUSED           = 7;
}

internal static class NativeServiceStartType
{
    public const uint SERVICE_BOOT_START   = 0;
    public const uint SERVICE_SYSTEM_START = 1;
    public const uint SERVICE_AUTO_START   = 2;
    public const uint SERVICE_DEMAND_START = 3;
    public const uint SERVICE_DISABLED     = 4;
}
