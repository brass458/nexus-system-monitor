namespace NexusMonitor.Core.Automation;

/// <summary>
/// Null implementation of IForegroundWindowProvider used on platforms where
/// native foreground-window detection is not yet available (macOS stub, Linux).
/// Returns 0 so all processes are treated as background candidates.
/// </summary>
public sealed class MockForegroundWindowProvider : IForegroundWindowProvider
{
    public int GetForegroundProcessId() => 0;
}
