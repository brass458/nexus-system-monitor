using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Platform.MacOS;

public sealed class MacOSPlatformCapabilities : IPlatformCapabilities
{
    public bool SupportsCpuAffinity        => false;
    public bool SupportsTrimMemory         => false;
    public bool SupportsCreateDump         => false;
    public bool SupportsFindWindow         => false;
    public bool SupportsIoPriority         => false;
    public bool SupportsMemoryPriority     => false;
    public bool UsesMetaKey                => true;
    public string FileManagerName          => "Finder";
    public string ServiceManagerName       => "Launch Daemons";
    public bool SupportsServiceStartupType => false;
}
