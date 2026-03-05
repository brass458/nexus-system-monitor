using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Health;

/// <summary>
/// Classifies the current user workload by examining the foreground process name
/// and the overall GPU/CPU utilisation pattern.
/// </summary>
public static class WorkloadClassifier
{
    // ── Known process name fragments, lower-case, no .exe ─────────────────────

    private static readonly HashSet<string> StreamingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "obs64", "obs32", "obs", "streamlabs obs", "streamlabs", "xsplit",
        "xsplit broadcaster", "prism live studio", "wirecast",
    };

    private static readonly HashSet<string> VideoEditingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "premiere pro", "premierecc", "afterfx", "aftereffects",
        "davinci resolve", "resolve", "vegaspro", "vegas", "vegas pro",
        "shotcut", "kdenlive", "handbrake", "media encoder", "adobemediacoder",
    };

    private static readonly HashSet<string> RenderingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "blender", "maya", "mayabatch", "3dsmax", "3dsmaxdesign",
        "cinema4d", "c4d", "houdini", "houdinifx", "v-ray", "vray",
        "corona renderer", "keyshot", "lumion", "twinmotion",
    };

    private static readonly HashSet<string> CadApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "autocad", "acad", "solidworks", "sldworks", "catia", "catmain",
        "ptc creo", "creo", "fusion360", "inventor", "revit", "navisworks",
        "ansys", "abaqus", "comsol", "matlab",
    };

    // ── Heuristic: high GPU usage + NOT one of the above = gaming ─────────────

    public static WorkloadType Classify(
        IReadOnlyList<ProcessInfo> processes,
        double gpuPercent,
        double cpuPercent,
        out string primaryProcessName)
    {
        primaryProcessName = string.Empty;

        // Find the top CPU+GPU consumer (most likely the workload driver)
        var top = processes
            .Where(p => p.Pid > 8 && !IsSystemProcess(p.Name))
            .OrderByDescending(p => p.CpuPercent * 0.4 + p.GpuPercent * 0.6)
            .FirstOrDefault();

        var topName = top?.Name ?? string.Empty;
        var topNameLower = topName.ToLowerInvariant();

        // Check explicit app lists first (most reliable)
        if (MatchesAny(topNameLower, StreamingApps))
        {
            primaryProcessName = topName;
            return WorkloadType.Streaming;
        }
        if (MatchesAny(topNameLower, VideoEditingApps))
        {
            primaryProcessName = topName;
            return WorkloadType.VideoEditing;
        }
        if (MatchesAny(topNameLower, RenderingApps))
        {
            primaryProcessName = topName;
            return WorkloadType.ThreeDRendering;
        }
        if (MatchesAny(topNameLower, CadApps))
        {
            primaryProcessName = topName;
            return WorkloadType.CadEngineering;
        }

        // Heuristic: sustained GPU + CPU load = gaming
        if (gpuPercent > 60 && cpuPercent > 30)
        {
            primaryProcessName = topName;
            return WorkloadType.Gaming;
        }

        // High CPU, low GPU = general compute
        if (cpuPercent > 60 && gpuPercent < 20)
        {
            primaryProcessName = topName;
            return WorkloadType.GeneralCompute;
        }

        return WorkloadType.Unknown;
    }

    private static bool MatchesAny(string processName, HashSet<string> list)
    {
        // Exact match or substring match (handles "adobe premiere pro" containing "premiere pro")
        if (list.Contains(processName)) return true;
        foreach (var entry in list)
            if (processName.Contains(entry, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsSystemProcess(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower is "system" or "idle" or "svchost" or "csrss" or "lsass"
            or "wininit" or "winlogon" or "explorer" or "dwm" or "registry";
    }

    public static string WorkloadLabel(WorkloadType t) => t switch
    {
        WorkloadType.Gaming         => "Gaming",
        WorkloadType.Streaming      => "Streaming",
        WorkloadType.VideoEditing   => "Video Editing",
        WorkloadType.ThreeDRendering => "3D Rendering",
        WorkloadType.CadEngineering => "CAD / Engineering",
        WorkloadType.GeneralCompute => "General Compute",
        _                           => "General Use",
    };
}
