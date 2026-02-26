using System.Runtime.InteropServices;

namespace NexusMonitor.Platform.Windows.Native;

internal static partial class NtDll
{
    private const string Dll = "ntdll.dll";

    // NTSTATUS success
    public const int STATUS_SUCCESS = 0;

    // ─── Process Suspend / Resume ─────────────────────────────────────────────

    [LibraryImport(Dll)]
    public static partial int NtSuspendProcess(nint ProcessHandle);

    [LibraryImport(Dll)]
    public static partial int NtResumeProcess(nint ProcessHandle);

    // ─── Process Information ──────────────────────────────────────────────────

    public enum PROCESSINFOCLASS
    {
        ProcessBasicInformation   = 0,
        ProcessCommandLineInfo    = 60,
    }

    [LibraryImport(Dll)]
    public static partial int NtQueryInformationProcess(
        nint ProcessHandle,
        PROCESSINFOCLASS ProcessInformationClass,
        nint ProcessInformation,
        uint ProcessInformationLength,
        out uint ReturnLength);

    // ─── System Information ───────────────────────────────────────────────────

    public enum SYSTEM_INFORMATION_CLASS
    {
        SystemBasicInformation      = 0,
        SystemPerformanceInformation = 2,
        SystemProcessInformation    = 5,
    }

    [LibraryImport(Dll)]
    public static partial int NtQuerySystemInformation(
        SYSTEM_INFORMATION_CLASS SystemInformationClass,
        nint SystemInformation,
        uint SystemInformationLength,
        out uint ReturnLength);
}
