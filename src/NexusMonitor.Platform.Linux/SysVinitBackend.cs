using System.Diagnostics;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Linux;

internal sealed class SysVinitBackend : ILinuxInitBackend
{
    public InitSystem System => InitSystem.SysVinit;

    public IReadOnlyList<ServiceInfo> EnumerateServices()
    {
        var result = new List<ServiceInfo>();
        const string initDir = "/etc/init.d";
        if (!Directory.Exists(initDir)) return result;

        try
        {
            foreach (var script in Directory.GetFiles(initDir))
            {
                var name = Path.GetFileName(script);
                // Skip dotfiles and README
                if (name.StartsWith('.') || name.Equals("README", StringComparison.OrdinalIgnoreCase))
                    continue;

                var running = IsRunning(name);

                result.Add(new ServiceInfo
                {
                    Name        = name,
                    DisplayName = name,
                    Description = string.Empty,
                    State       = running ? ServiceState.Running : ServiceState.Stopped,
                    StartType   = ServiceStartType.Manual,
                    ServiceType = ServiceType.Unknown,
                    ProcessId   = 0,
                    BinaryPath  = script,
                    UserAccount = string.Empty,
                });
            }
        }
        catch { }

        return result;
    }

    private static bool IsRunning(string name)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("service", $"{name} status")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            proc.WaitForExit(3000);
            // Exit code 0 means running for most SysV scripts
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    public void Start(string name)   => Run("service", $"{name} start");
    public void Stop(string name)    => Run("service", $"{name} stop");
    public void Restart(string name) => Run("service", $"{name} restart");

    public void SetStartType(string name, ServiceStartType startType)
    {
        // update-rc.d is the Debian/LSB way; chkconfig is the Red Hat way
        if (File.Exists("/usr/sbin/update-rc.d"))
        {
            var action = startType == ServiceStartType.Automatic ? "enable" : "disable";
            Run("update-rc.d", $"{name} {action}");
        }
        else if (File.Exists("/sbin/chkconfig"))
        {
            var onOff = startType == ServiceStartType.Automatic ? "on" : "off";
            Run("chkconfig", $"{name} {onOff}");
        }
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
