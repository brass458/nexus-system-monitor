using System.Diagnostics;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

internal sealed class OpenRcBackend : ILinuxInitBackend
{
    public InitSystem System => InitSystem.OpenRC;

    public IReadOnlyList<ServiceInfo> EnumerateServices()
    {
        var result = new List<ServiceInfo>();
        try
        {
            // rc-status --all lists services and their state
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("rc-status", "--all --nocolor")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();

            var seen   = new HashSet<string>(StringComparer.Ordinal);
            string? line;
            while ((line = proc.StandardOutput.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // Lines look like: " sshd                         [ started ]"
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("Runlevel:", StringComparison.OrdinalIgnoreCase)) continue;
                if (!trimmed.Contains('[')) continue;

                var bracketStart = trimmed.IndexOf('[');
                var bracketEnd   = trimmed.IndexOf(']', bracketStart);
                if (bracketStart < 0 || bracketEnd < 0) continue;

                var svcName  = trimmed[..bracketStart].Trim();
                var stateStr = trimmed[(bracketStart + 1)..bracketEnd].Trim();
                if (string.IsNullOrEmpty(svcName) || !seen.Add(svcName)) continue;

                var state = stateStr.Equals("started", StringComparison.OrdinalIgnoreCase)
                    ? ServiceState.Running
                    : ServiceState.Stopped;

                result.Add(new ServiceInfo
                {
                    Name        = svcName,
                    DisplayName = svcName,
                    Description = string.Empty,
                    State       = state,
                    StartType   = ServiceStartType.Manual,
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

    public void Start(string name)   => Run("rc-service", $"{name} start");
    public void Stop(string name)    => Run("rc-service", $"{name} stop");
    public void Restart(string name) => Run("rc-service", $"{name} restart");

    public void SetStartType(string name, ServiceStartType startType)
    {
        // rc-update add <name> default  /  rc-update del <name>
        if (startType == ServiceStartType.Automatic)
            Run("rc-update", $"add {name} default");
        else
            Run("rc-update", $"del {name}");
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
}
