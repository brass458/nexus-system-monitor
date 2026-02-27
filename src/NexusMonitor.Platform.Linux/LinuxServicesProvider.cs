using System.Diagnostics;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

public sealed class LinuxServicesProvider : IServicesProvider
{
    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ServiceInfo>>(EnumerateServices, ct);

    private static IReadOnlyList<ServiceInfo> EnumerateServices()
    {
        var result = new List<ServiceInfo>();
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo(
                    "systemctl",
                    "list-units --type=service --all --no-pager --no-legend --plain")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();

            string? line;
            while ((line = proc.StandardOutput.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.TrimStart().Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                var unitName = parts[0].Replace(".service", "", StringComparison.OrdinalIgnoreCase);
                var load     = parts[1];
                var active   = parts[2];
                var sub      = parts[3];
                var desc     = parts.Length > 4 ? parts[4] : unitName;

                // Ignore unloaded / not-found units
                if (load.Equals("not-found", StringComparison.OrdinalIgnoreCase)) continue;

                var state = active.Equals("active", StringComparison.OrdinalIgnoreCase)
                    ? ServiceState.Running
                    : ServiceState.Stopped;

                var startType = load.Equals("enabled", StringComparison.OrdinalIgnoreCase)
                    ? ServiceStartType.Automatic
                    : ServiceStartType.Manual;

                result.Add(new ServiceInfo
                {
                    Name        = unitName,
                    DisplayName = desc,
                    Description = desc,
                    State       = state,
                    StartType   = startType,
                    ServiceType = ServiceType.Unknown,
                    ProcessId   = 0,
                    BinaryPath  = string.Empty,
                    UserAccount = string.Empty,
                });
            }

            proc.WaitForExit(5000);
        }
        catch { }

        return result;
    }

    public Task StartServiceAsync(string name, CancellationToken ct = default) =>
        RunSystemctl("start", name, ct);

    public Task StopServiceAsync(string name, CancellationToken ct = default) =>
        RunSystemctl("stop", name, ct);

    public Task RestartServiceAsync(string name, CancellationToken ct = default) =>
        RunSystemctl("restart", name, ct);

    public Task SetStartTypeAsync(string name, ServiceStartType startType, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var verb = startType == ServiceStartType.Automatic ? "enable" : "disable";
            try
            {
                using var proc = Process.Start(
                    new ProcessStartInfo("systemctl", $"{verb} {name}.service")
                    {
                        UseShellExecute = false,
                        CreateNoWindow  = true,
                    });
                proc?.WaitForExit(5000);
            }
            catch { }
        }, ct);

    private static Task RunSystemctl(string verb, string name, CancellationToken ct) =>
        Task.Run(() =>
        {
            try
            {
                using var proc = Process.Start(
                    new ProcessStartInfo("systemctl", $"{verb} {name}.service")
                    {
                        UseShellExecute = false,
                        CreateNoWindow  = true,
                    });
                proc?.WaitForExit(5000);
            }
            catch { }
        }, ct);
}
