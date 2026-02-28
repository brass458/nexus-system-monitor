namespace NexusMonitor.Core.Automation;

/// <summary>Platform-specific foreground window detection for ProBalance.</summary>
public interface IForegroundWindowProvider
{
    /// <summary>Returns the PID of the process that owns the current foreground window, or 0 if unknown.</summary>
    int GetForegroundProcessId();
}
