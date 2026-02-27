using System.Runtime.InteropServices;

namespace NexusMonitor.Platform.Windows.Native;

internal static partial class PsApi
{
    private const string Dll = "psapi.dll";

    // ─── Memory ───────────────────────────────────────────────────────────────

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessMemoryInfo(
        nint hProcess,
        ref PROCESS_MEMORY_COUNTERS_EX ppsmemCounters,
        uint cb);

    // ─── Module enumeration ───────────────────────────────────────────────────

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumProcessModules(
        nint hProcess,
        [Out] nint[] lphModule,
        uint cb,
        out uint lpcbNeeded);

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetModuleFileNameExW(
        nint hProcess,
        nint hModule,
        [Out] char[] lpFilename,
        uint nSize);

    // ─── Performance counters ─────────────────────────────────────────────────

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPerformanceInfo(
        ref PERFORMANCE_INFORMATION pPerformanceInformation,
        uint cb);

    // ─── Memory-mapped file name ──────────────────────────────────────────────
    // Uses DllImport for reliable char[] output marshalling.

    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint GetMappedFileNameW(
        nint hProcess,
        nint lpv,
        [Out] char[] lpFilename,
        uint nSize);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PERFORMANCE_INFORMATION
{
    public uint  cb;
    public nuint CommitTotal;
    public nuint CommitLimit;
    public nuint CommitPeak;
    public nuint PhysicalTotal;
    public nuint PhysicalAvailable;
    public nuint SystemCache;
    public nuint KernelTotal;
    public nuint KernelPaged;
    public nuint KernelNonpaged;
    public nuint PageSize;
    public uint  HandleCount;
    public uint  ProcessCount;
    public uint  ThreadCount;
}
