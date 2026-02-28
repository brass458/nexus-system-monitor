using System.Runtime.InteropServices;

namespace NexusMonitor.Platform.Windows.Native;

internal static partial class DbgHelp
{
    [LibraryImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MiniDumpWriteDump(
        nint hProcess,
        uint processId,
        nint hFile,
        uint dumpType,
        nint exceptionParam,
        nint userStreamParam,
        nint callbackParam);

    // Common dump types
    internal const uint MiniDumpNormal           = 0x00000000;
    internal const uint MiniDumpWithDataSegs      = 0x00000001;
    internal const uint MiniDumpWithFullMemory    = 0x00000002;
    internal const uint MiniDumpWithHandleData    = 0x00000004;
}
