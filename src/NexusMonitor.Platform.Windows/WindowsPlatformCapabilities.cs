using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Platform.Windows;

public sealed class WindowsPlatformCapabilities : IPlatformCapabilities
{
    public bool SupportsCpuAffinity       => true;
    public bool SupportsTrimMemory        => true;
    public bool SupportsCreateDump        => true;
    public bool SupportsFindWindow        => true;
    public bool SupportsIoPriority        => true;
    public bool SupportsMemoryPriority    => true;
    public bool UsesMetaKey               => false;
    public string FileManagerName         => "Explorer";
    public string ServiceManagerName      => "Services";
    public bool SupportsServiceStartupType => true;
}
