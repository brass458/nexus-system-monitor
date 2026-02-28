using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

internal enum InitSystem { Systemd, SysVinit, OpenRC, Unknown }

internal interface ILinuxInitBackend
{
    InitSystem System { get; }
    IReadOnlyList<ServiceInfo> EnumerateServices();
    void Start(string name);
    void Stop(string name);
    void Restart(string name);
    void SetStartType(string name, ServiceStartType startType);
}
