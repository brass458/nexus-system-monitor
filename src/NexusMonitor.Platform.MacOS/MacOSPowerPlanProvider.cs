using System.Diagnostics;
using NexusMonitor.Core.Gaming;

namespace NexusMonitor.Platform.MacOS;

/// <summary>
/// macOS power plan provider backed by pmset.
/// Three virtual profiles mapped to macOS power settings:
///   Power Saver      → Low Power Mode on  (pmset -a lowpowermode 1)
///   Balanced         → Default            (pmset -a lowpowermode 0)
///   High Performance → Low Power Mode off, prevent display sleep
/// pmset may silently fail without elevated privileges — acceptable behaviour.
/// </summary>
public sealed class MacOSPowerPlanProvider : IPowerPlanProvider
{
    private Guid _active;

    public MacOSPowerPlanProvider()
    {
        _active = ReadActive();
    }

    public IReadOnlyList<PowerPlanInfo> GetPowerPlans()
    {
        var current = GetActivePlan();
        return
        [
            new(IPowerPlanProvider.PowerSaver,      "Power Saver",      current == IPowerPlanProvider.PowerSaver),
            new(IPowerPlanProvider.Balanced,         "Balanced",         current == IPowerPlanProvider.Balanced),
            new(IPowerPlanProvider.HighPerformance,  "High Performance", current == IPowerPlanProvider.HighPerformance),
        ];
    }

    public Guid GetActivePlan() => _active;

    public void SetActivePlan(Guid schemeGuid)
    {
        _active = schemeGuid;
        ApplyPlan(schemeGuid);
    }

    // ── pmset helpers ──────────────────────────────────────────────────────────
    private static Guid ReadActive()
    {
        try
        {
            var output = RunCapture("pmset", "-g");
            // Look for " lowpowermode  1" in output
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("lowpowermode", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[1] == "1")
                        return IPowerPlanProvider.PowerSaver;
                    break;
                }
            }
        }
        catch { }
        return IPowerPlanProvider.Balanced;
    }

    private static void ApplyPlan(Guid schemeGuid)
    {
        if (schemeGuid == IPowerPlanProvider.PowerSaver)
        {
            Run("pmset", "-a lowpowermode 1");
        }
        else if (schemeGuid == IPowerPlanProvider.HighPerformance)
        {
            Run("pmset", "-a lowpowermode 0");
            Run("pmset", "-a sleep 0 disksleep 0 displaysleep 0");
        }
        else
        {
            // Balanced — restore Low Power Mode off; leave other settings at defaults
            Run("pmset", "-a lowpowermode 0");
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
            proc?.WaitForExit(3000);
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
