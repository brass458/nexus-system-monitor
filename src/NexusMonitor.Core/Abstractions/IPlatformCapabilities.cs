namespace NexusMonitor.Core.Abstractions;

/// <summary>
/// Describes which features are available on the current platform.
/// Injected into ViewModels to gate UI elements at runtime.
/// </summary>
public interface IPlatformCapabilities
{
    bool SupportsCpuAffinity  { get; }
    bool SupportsTrimMemory   { get; }
    bool SupportsCreateDump   { get; }
    bool SupportsFindWindow   { get; }
    bool SupportsIoPriority   { get; }
    bool SupportsMemoryPriority { get; }
    /// <summary>True on macOS where the Meta (Cmd) key is the primary modifier.</summary>
    bool UsesMetaKey          { get; }
    /// <summary>Platform name for the file manager (e.g. "Explorer", "Finder", "Files").</summary>
    string FileManagerName    { get; }
    /// <summary>Platform name for the service manager (e.g. "Services", "Launch Daemons", "systemd").</summary>
    string ServiceManagerName { get; }
    /// <summary>True if the Services tab's startup-type submenu is supported on this platform.</summary>
    bool SupportsServiceStartupType { get; }
}

/// <summary>Full-featured fallback used in mock/design-time builds.</summary>
public sealed class MockPlatformCapabilities : IPlatformCapabilities
{
    public bool SupportsCpuAffinity        => true;
    public bool SupportsTrimMemory         => true;
    public bool SupportsCreateDump         => true;
    public bool SupportsFindWindow         => true;
    public bool SupportsIoPriority         => true;
    public bool SupportsMemoryPriority     => true;
    public bool UsesMetaKey                => false;
    public string FileManagerName          => "Explorer";
    public string ServiceManagerName       => "Services";
    public bool SupportsServiceStartupType => true;
}
