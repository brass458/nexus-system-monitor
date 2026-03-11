using System.Diagnostics;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

internal sealed class SystemdBackend : ILinuxInitBackend
{
    public InitSystem System => InitSystem.Systemd;

    public IReadOnlyList<ServiceInfo> EnumerateServices()
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
                var desc     = parts.Length > 4 ? parts[4] : unitName;

                if (load.Equals("not-found", StringComparison.OrdinalIgnoreCase)) continue;

                var state = active.Equals("active", StringComparison.OrdinalIgnoreCase)
                    ? ServiceState.Running
                    : ServiceState.Stopped;

                // Fetch PID, binary path, and unit file state via systemctl show
                int    pid        = 0;
                string binaryPath = string.Empty;
                var    startType  = ServiceStartType.Manual;
                try
                {
                    var showOut = RunCapture("systemctl",
                        $"show {unitName}.service --property=MainPID,ExecStart,UnitFileState --no-pager");
                    foreach (var showLine in showOut.Split('\n'))
                    {
                        if (showLine.StartsWith("MainPID=", StringComparison.Ordinal))
                            int.TryParse(showLine[8..].Trim(), out pid);
                        else if (showLine.StartsWith("ExecStart=", StringComparison.Ordinal))
                        {
                            // ExecStart={ path=... ; argv[]= ... }
                            var pathIdx = showLine.IndexOf("path=", StringComparison.Ordinal);
                            if (pathIdx >= 0)
                            {
                                var rest = showLine[(pathIdx + 5)..];
                                var end  = rest.IndexOfAny([' ', ';']);
                                binaryPath = end >= 0 ? rest[..end] : rest;
                            }
                        }
                        else if (showLine.StartsWith("UnitFileState=", StringComparison.Ordinal))
                        {
                            var unitFileState = showLine[14..].Trim();
                            startType = unitFileState.Equals("enabled", StringComparison.OrdinalIgnoreCase)
                                ? ServiceStartType.Automatic
                                : ServiceStartType.Manual;
                        }
                    }
                }
                catch { }

                result.Add(new ServiceInfo
                {
                    Name        = unitName,
                    DisplayName = desc,
                    Description = desc,
                    State       = state,
                    StartType   = startType,
                    ServiceType = ServiceType.Unknown,
                    ProcessId   = pid,
                    BinaryPath  = binaryPath,
                    UserAccount = string.Empty,
                });
            }

            proc.WaitForExit(5000);
        }
        catch { }

        return result;
    }

    public void Start(string name)   => Run("systemctl", $"start {name}.service");
    public void Stop(string name)    => Run("systemctl", $"stop {name}.service");
    public void Restart(string name) => Run("systemctl", $"restart {name}.service");

    public void SetStartType(string name, ServiceStartType startType)
    {
        var verb = startType == ServiceStartType.Automatic ? "enable" : "disable";
        Run("systemctl", $"{verb} {name}.service");
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
            var outputTask = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(3000)) { try { proc.Kill(); } catch { } }
            return outputTask.Result;
        }
        catch { return string.Empty; }
    }
}
