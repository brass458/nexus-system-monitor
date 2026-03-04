using System.Diagnostics;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

internal sealed class RunitBackend : ILinuxInitBackend
{
    public InitSystem System => InitSystem.Runit;

    // runit service directories
    private static readonly string[] _svcDirs = ["/var/service", "/service", "/etc/sv"];

    public IReadOnlyList<ServiceInfo> EnumerateServices()
    {
        var result = new List<ServiceInfo>();
        try
        {
            // /var/service (or /service) contains symlinks to enabled services
            var activeDir = _svcDirs.FirstOrDefault(Directory.Exists) ?? "/var/service";
            if (!Directory.Exists(activeDir)) return result;

            foreach (var dir in Directory.GetDirectories(activeDir))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;

                // Check if running: sv status <name> exits 0 and prints "run: ..."
                var status = RunCapture("sv", $"status {name}");
                var isRunning = status.StartsWith("run:", StringComparison.OrdinalIgnoreCase);

                // Check if enabled: symlink exists in /var/service
                var isEnabled = Directory.Exists(Path.Combine(activeDir, name));

                result.Add(new ServiceInfo
                {
                    Name        = name,
                    DisplayName = name,
                    Description = string.Empty,
                    State       = isRunning ? ServiceState.Running : ServiceState.Stopped,
                    StartType   = isEnabled ? ServiceStartType.Automatic : ServiceStartType.Manual,
                    ServiceType = ServiceType.Unknown,
                    ProcessId   = 0,
                    BinaryPath  = string.Empty,
                    UserAccount = string.Empty,
                });
            }
        }
        catch { }
        return result;
    }

    public void Start(string name)   => Run("sv", $"start {name}");
    public void Stop(string name)    => Run("sv", $"stop {name}");
    public void Restart(string name) => Run("sv", $"restart {name}");

    public void SetStartType(string name, ServiceStartType startType)
    {
        // Enable: create symlink in /var/service; Disable: remove it
        var svDir  = _svcDirs.FirstOrDefault(Directory.Exists) ?? "/var/service";
        var link   = Path.Combine(svDir, name);
        var source = Path.Combine("/etc/sv", name);
        try
        {
            if (startType == ServiceStartType.Automatic)
            {
                if (!Directory.Exists(link) && Directory.Exists(source))
                    Directory.CreateSymbolicLink(link, source);
            }
            else
            {
                if (Directory.Exists(link))
                    Directory.Delete(link);
            }
        }
        catch { }
    }

    private static void Run(string cmd, string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(cmd, args)
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
            proc?.WaitForExit(5000);
        }
        catch { }
    }

    private static string RunCapture(string cmd, string args)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo(cmd, args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output;
        }
        catch { return string.Empty; }
    }
}
