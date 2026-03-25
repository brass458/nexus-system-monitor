using System.Diagnostics;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

internal sealed class DinitBackend : ILinuxInitBackend
{
    public InitSystem System => InitSystem.Dinit;

    public IReadOnlyList<ServiceInfo> EnumerateServices()
    {
        var result = new List<ServiceInfo>();
        try
        {
            var output = RunCapture("dinitctl", "list");
            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                // Format: "[[started]  ] service-name" or "[{stopped}  ] service-name"
                var trimmed = line.TrimStart();
                var stateEnd = trimmed.IndexOf(']');
                if (stateEnd < 0) continue;

                var stateStr = trimmed[..(stateEnd + 1)];
                var rest     = trimmed[(stateEnd + 1)..].Trim();
                if (string.IsNullOrEmpty(rest)) continue;

                // Name is the first word
                var nameParts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var name = nameParts[0];

                // dinitctl list format: "[{+}  ]" for started, "[{-}  ]" for stopped.
                // Older versions may print "started"/"stopped" as text — handle both.
                var isRunning = stateStr.Contains('+')
                             || stateStr.Contains("started", StringComparison.OrdinalIgnoreCase);

                result.Add(new ServiceInfo
                {
                    Name        = name,
                    DisplayName = name,
                    Description = string.Empty,
                    State       = isRunning ? ServiceState.Running : ServiceState.Stopped,
                    StartType   = ServiceStartType.Manual,
                    ServiceType = ServiceType.Unknown,
                    ProcessId   = 0,
                    BinaryPath  = string.Empty,
                    UserAccount = string.Empty,
                });
            }
        }
        catch (Exception ex) { /* ignored: dinitctl enumeration failed */ _ = ex; }
        return result;
    }

    public void Start(string name)   => Run("dinitctl", $"start {name}");
    public void Stop(string name)    => Run("dinitctl", $"stop {name}");
    public void Restart(string name) => Run("dinitctl", $"restart {name}");

    public void SetStartType(string name, ServiceStartType startType)
    {
        var verb = startType == ServiceStartType.Automatic ? "enable" : "disable";
        Run("dinitctl", $"{verb} {name}");
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
        catch (Exception ex) { /* ignored: dinitctl command execution failed */ _ = ex; }
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
            var outputTask = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(3000)) { try { proc.Kill(); } catch (Exception ex) { /* ignored: process kill failed */ _ = ex; } }
            return outputTask.Result;
        }
        catch (Exception ex) { /* ignored: dinitctl command execution failed, return empty */ _ = ex; return string.Empty; }
    }
}
