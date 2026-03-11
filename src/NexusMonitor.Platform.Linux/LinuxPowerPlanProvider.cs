using System.Diagnostics;
using NexusMonitor.Core.Gaming;

namespace NexusMonitor.Platform.Linux;

/// <summary>
/// Power plan provider for Linux.
/// Primary: power-profiles-daemon (powerprofilesctl).
/// Fallback: cpufreq scaling_governor via /sys.
/// If neither available: behaves like MockPowerPlanProvider (show plans, no-op set).
/// </summary>
public sealed class LinuxPowerPlanProvider : IPowerPlanProvider
{
    private enum Backend { PowerProfilesDaemon, CpuFreq, Mock }

    private readonly Backend _backend;
    private Guid _active;

    private static readonly IReadOnlyList<PowerPlanInfo> _plans =
    [
        new(IPowerPlanProvider.PowerSaver,      "Power Saver",      false),
        new(IPowerPlanProvider.Balanced,         "Balanced",         true),
        new(IPowerPlanProvider.HighPerformance,  "High Performance", false),
    ];

    public LinuxPowerPlanProvider()
    {
        if (IsPowerProfilesDaemonAvailable())
        {
            _backend = Backend.PowerProfilesDaemon;
            _active  = ReadActivePowerProfilesDaemon();
        }
        else if (IsCpuFreqAvailable())
        {
            _backend = Backend.CpuFreq;
            _active  = ReadActiveCpuFreq();
        }
        else
        {
            _backend = Backend.Mock;
            _active  = IPowerPlanProvider.Balanced;
        }
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
        switch (_backend)
        {
            case Backend.PowerProfilesDaemon:
                SetPowerProfilesDaemon(schemeGuid);
                break;
            case Backend.CpuFreq:
                SetCpuFreq(schemeGuid);
                break;
            // Mock: no-op
        }
        _active = schemeGuid; // only update after system call succeeds
    }

    // ── power-profiles-daemon ──────────────────────────────────────────────────
    private static bool IsPowerProfilesDaemonAvailable()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("powerprofilesctl", "get")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            proc.WaitForExit(2000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private static Guid ReadActivePowerProfilesDaemon()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("powerprofilesctl", "get")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            var outputTask = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(2000)) { try { proc.Kill(); } catch { } }
            return MapPowerProfileToGuid(outputTask.Result.Trim());
        }
        catch { return IPowerPlanProvider.Balanced; }
    }

    private static Guid MapPowerProfileToGuid(string profile) => profile switch
    {
        "power-saver"  => IPowerPlanProvider.PowerSaver,
        "performance"  => IPowerPlanProvider.HighPerformance,
        _              => IPowerPlanProvider.Balanced,  // "balanced" and unknown
    };

    private static string MapGuidToPowerProfile(Guid guid)
    {
        if (guid == IPowerPlanProvider.PowerSaver)      return "power-saver";
        if (guid == IPowerPlanProvider.HighPerformance) return "performance";
        return "balanced";
    }

    private static void SetPowerProfilesDaemon(Guid schemeGuid)
    {
        var profile = MapGuidToPowerProfile(schemeGuid);
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("powerprofilesctl", $"set {profile}")
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
            proc?.WaitForExit(3000);
        }
        catch { }
    }

    // ── cpufreq ────────────────────────────────────────────────────────────────
    private const string GovernorPath = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor";

    private static bool IsCpuFreqAvailable() => File.Exists(GovernorPath);

    private static Guid ReadActiveCpuFreq()
    {
        try
        {
            var governor = File.ReadAllText(GovernorPath).Trim();
            return governor switch
            {
                "powersave"                        => IPowerPlanProvider.PowerSaver,
                "conservative"                     => IPowerPlanProvider.PowerSaver,
                "schedutil" or "ondemand"          => IPowerPlanProvider.Balanced,
                "performance"                      => IPowerPlanProvider.HighPerformance,
                _                                  => IPowerPlanProvider.Balanced,
            };
        }
        catch { return IPowerPlanProvider.Balanced; }
    }

    private static void SetCpuFreq(Guid schemeGuid)
    {
        string governor;
        if (schemeGuid == IPowerPlanProvider.PowerSaver)
            governor = "powersave";
        else if (schemeGuid == IPowerPlanProvider.HighPerformance)
            governor = "performance";
        else
            governor = "schedutil";

        // Write to all CPU cores
        try
        {
            foreach (var cpuDir in Directory.GetDirectories("/sys/devices/system/cpu", "cpu*"))
            {
                var path = Path.Combine(cpuDir, "cpufreq", "scaling_governor");
                if (File.Exists(path))
                {
                    try { File.WriteAllText(path, governor); }
                    catch { }
                }
            }
        }
        catch { }
    }
}
