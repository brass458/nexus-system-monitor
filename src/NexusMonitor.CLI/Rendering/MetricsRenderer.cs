using Spectre.Console;

namespace NexusMonitor.CLI.Rendering;

/// <summary>
/// Static helpers for formatting metrics for Spectre.Console output.
/// </summary>
internal static class MetricsRenderer
{
    /// <summary>Formats a byte count as a human-readable string (B/KB/MB/GB/TB).</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024L)
            return $"{bytes} B";
        if (bytes < 1024L * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
    }

    /// <summary>Formats a bytes-per-second rate as a human-readable string.</summary>
    public static string FormatBytesPerSec(long bytesPerSec)
        => $"{FormatBytes(bytesPerSec)}/s";

    /// <summary>
    /// Returns an ASCII progress bar like <c>[████░░░░░░] 42%</c>.
    /// </summary>
    public static string PercentBar(double percent, int width = 20)
    {
        percent = Math.Clamp(percent, 0, 100);
        int filled = (int)Math.Round(percent / 100.0 * width);
        var bar = new string('\u2588', filled) + new string('\u2591', width - filled);
        return $"[{bar}] {percent,5:F1}%";
    }

    /// <summary>
    /// Returns a Spectre.Console markup string for a health score bar,
    /// colored green (score &gt; 80), yellow (score &gt; 50), or red otherwise.
    /// </summary>
    public static string HealthScoreBar(double score, int width = 20)
    {
        score = Math.Clamp(score, 0, 100);
        int filled = (int)Math.Round(score / 100.0 * width);
        var bar = new string('\u2588', filled) + new string('\u2591', width - filled);
        string color = score > 80 ? "green" : score > 50 ? "yellow" : "red";
        return $"[{color}][{bar}][/] {score,5:F1}";
    }

    /// <summary>
    /// Returns a Spectre.Console markup string for a percentage bar with color coding.
    /// High usage (&gt;80%) is red, moderate (&gt;50%) is yellow, low is green.
    /// </summary>
    public static string ColoredPercentBar(double percent, int width = 20)
    {
        percent = Math.Clamp(percent, 0, 100);
        int filled = (int)Math.Round(percent / 100.0 * width);
        var bar = new string('\u2588', filled) + new string('\u2591', width - filled);
        string color = percent > 80 ? "red" : percent > 50 ? "yellow" : "green";
        return $"[{color}][{bar}][/] {percent,5:F1}%";
    }
}
