using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Platform.Linux;

public sealed class LinuxPlatformCapabilities : IPlatformCapabilities
{
    public bool SupportsCpuAffinity        => true;
    public bool SupportsTrimMemory         => false;
    public bool SupportsCreateDump         => false;
    public bool SupportsFindWindow         => false;
    public bool SupportsIoPriority         => true;
    public bool SupportsMemoryPriority     => false;
    public bool UsesMetaKey                => false;
    public string FileManagerName          => "Files";
    public string ServiceManagerName       => "systemd";
    public bool SupportsServiceStartupType => true;
    public bool SupportsRegistry           => false;
    public bool SupportsEfficiencyMode     => false;
    public bool SupportsHandles            => false;
    public bool SupportsMemoryMap          => false;
    public bool SupportsPowerPlan          => true;
    public string OpenLocationMenuLabel    => "Show in Files";
}
