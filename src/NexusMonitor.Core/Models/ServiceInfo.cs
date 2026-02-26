namespace NexusMonitor.Core.Models;

public enum ServiceState { Running, Stopped, Paused, StartPending, StopPending, Unknown }
public enum ServiceStartType { Automatic, Manual, Disabled, AutomaticDelayed, Unknown }
public enum ServiceType { Win32OwnProcess, Win32ShareProcess, KernelDriver, FileSystemDriver, Unknown }

public record ServiceInfo
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ServiceState State { get; init; }
    public ServiceStartType StartType { get; init; }
    public ServiceType ServiceType { get; init; }
    public int ProcessId { get; init; }
    public string BinaryPath { get; init; } = string.Empty;
    public string UserAccount { get; init; } = string.Empty;
}
