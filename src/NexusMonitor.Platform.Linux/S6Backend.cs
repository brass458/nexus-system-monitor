using System.Diagnostics;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

internal sealed class S6Backend : ILinuxInitBackend
{
    public InitSystem System => InitSystem.S6;

    public IReadOnlyList<ServiceInfo> EnumerateServices()
    {
        var result = new List<ServiceInfo>();
        try
        {
            // s6-rc -a list lists active services
            var activeOutput = RunCapture("s6-rc", "-a list");
            var activeSet    = new HashSet<string>(
                activeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            // s6-rc list lists all known services
            var allOutput = RunCapture("s6-rc", "list");
            foreach (var line in allOutput.Split('\n'))
            {
                var name = line.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                result.Add(new ServiceInfo
                {
                    Name        = name,
                    DisplayName = name,
                    Description = string.Empty,
                    State       = activeSet.Contains(name) ? ServiceState.Running : ServiceState.Stopped,
                    StartType   = ServiceStartType.Manual,
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

    public void Start(string name)   => Run("s6-rc", $"-u change {name}");
    public void Stop(string name)    => Run("s6-rc", $"-d change {name}");
    public void Restart(string name)
    {
        Run("s6-svc", $"-r /run/service/{name}");
    }

    public void SetStartType(string name, ServiceStartType startType)
    {
        // s6-rc doesn't have a simple enable/disable — no-op for now
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
