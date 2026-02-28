using NexusMonitor.Core.Automation;

namespace NexusMonitor.Platform.MacOS;

/// <summary>
/// Stub implementation — returning foreground PID via NSWorkspace requires ObjC messaging
/// which is beyond the scope of this provider. Returns 0 (unknown) for now.
/// </summary>
public sealed class MacOSForegroundWindowProvider : IForegroundWindowProvider
{
    public int GetForegroundProcessId() => 0;
    // TODO: Use objc_msgSend to call
    //   [NSWorkspace sharedWorkspace].frontmostApplication.processIdentifier
}
