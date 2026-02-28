using System.Runtime.InteropServices;
using NexusMonitor.Core.Automation;

namespace NexusMonitor.Platform.MacOS;

/// <summary>
/// Returns the PID of the frontmost application via ObjC runtime messaging:
///   [[NSWorkspace sharedWorkspace] frontmostApplication].processIdentifier
/// </summary>
public sealed class MacOSForegroundWindowProvider : IForegroundWindowProvider
{
    // ── ObjC runtime P/Invoke ──────────────────────────────────────────────────
    [DllImport("libobjc.A.dylib")]
    private static extern nint objc_getClass(string name);

    [DllImport("libobjc.A.dylib")]
    private static extern nint sel_registerName(string name);

    [DllImport("libobjc.A.dylib")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport("libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern int objc_msgSend_int(nint receiver, nint selector);

    // ── Cached selectors (created once) ───────────────────────────────────────
    private static readonly nint s_selSharedWorkspace      = sel_registerName("sharedWorkspace");
    private static readonly nint s_selFrontmostApplication = sel_registerName("frontmostApplication");
    private static readonly nint s_selProcessIdentifier    = sel_registerName("processIdentifier");

    private static readonly nint s_nsWorkspaceClass = objc_getClass("NSWorkspace");

    public int GetForegroundProcessId()
    {
        try
        {
            // [NSWorkspace sharedWorkspace]
            var workspace = objc_msgSend(s_nsWorkspaceClass, s_selSharedWorkspace);
            if (workspace == nint.Zero) return 0;

            // [workspace frontmostApplication]
            var frontApp = objc_msgSend(workspace, s_selFrontmostApplication);
            if (frontApp == nint.Zero) return 0;

            // [frontApp processIdentifier]  — returns pid_t (int)
            return objc_msgSend_int(frontApp, s_selProcessIdentifier);
        }
        catch
        {
            return 0;
        }
    }
}
