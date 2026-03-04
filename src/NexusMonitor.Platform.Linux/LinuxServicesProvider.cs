using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

public sealed class LinuxServicesProvider : IServicesProvider
{
    private readonly ILinuxInitBackend _backend = DetectBackend();

    // ── Init-system detection ──────────────────────────────────────────────────
    private static ILinuxInitBackend DetectBackend()
    {
        try
        {
            // 1. Check /proc/1/comm — the init process name
            if (File.Exists("/proc/1/comm"))
            {
                var comm = File.ReadAllText("/proc/1/comm").Trim();
                if (comm.Equals("systemd", StringComparison.OrdinalIgnoreCase))
                    return new SystemdBackend();
                if (comm.Equals("dinit", StringComparison.OrdinalIgnoreCase))
                    return new DinitBackend();
                if (comm.Equals("runit", StringComparison.OrdinalIgnoreCase))
                    return new RunitBackend();
                if (comm.StartsWith("s6-", StringComparison.OrdinalIgnoreCase))
                    return new S6Backend();
                if (comm.Contains("openrc", StringComparison.OrdinalIgnoreCase))
                    return new OpenRcBackend();
            }

            // 2. Check for systemd socket directory
            if (Directory.Exists("/run/systemd/system"))
                return new SystemdBackend();

            // 3. Check for dinit socket
            if (Directory.Exists("/run/dinitctl") || File.Exists("/run/dinit/dinit.sock"))
                return new DinitBackend();

            // 4. Check for runit service directory
            if (Directory.Exists("/etc/sv") && Directory.Exists("/var/service"))
                return new RunitBackend();

            // 5. Check for s6 service directory
            if (Directory.Exists("/run/s6") || Directory.Exists("/etc/s6"))
                return new S6Backend();

            // 6. Check for OpenRC softlevel file
            if (File.Exists("/run/openrc/softlevel"))
                return new OpenRcBackend();
        }
        catch { }

        // 7. Fall back to SysVinit if /etc/init.d exists, otherwise systemd
        return Directory.Exists("/etc/init.d")
            ? new SysVinitBackend()
            : new SystemdBackend();
    }

    // ── IServicesProvider ──────────────────────────────────────────────────────
    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ServiceInfo>>(_backend.EnumerateServices, ct);

    public Task StartServiceAsync(string name, CancellationToken ct = default) =>
        Task.Run(() => _backend.Start(name), ct);

    public Task StopServiceAsync(string name, CancellationToken ct = default) =>
        Task.Run(() => _backend.Stop(name), ct);

    public Task RestartServiceAsync(string name, CancellationToken ct = default) =>
        Task.Run(() => _backend.Restart(name), ct);

    public Task SetStartTypeAsync(string name, ServiceStartType startType, CancellationToken ct = default) =>
        Task.Run(() => _backend.SetStartType(name, startType), ct);
}
