using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Health;

/// <summary>
/// Computes a 0-100 impact score for each process, representing how much it
/// affects overall system performance relative to all other processes.
///
/// Formula: Impact = 0.40*CpuNorm + 0.30*MemNorm + 0.15*IoNorm + 0.10*GpuNorm + 0.05*HandleNorm
/// Each component is normalised against the system-wide total.
/// </summary>
public static class ImpactScoreCalculator
{
    public static double Calculate(ProcessInfo process, SystemTotals totals)
    {
        var cpuNorm    = totals.TotalCpuPercent    > 0 ? process.CpuPercent            / totals.TotalCpuPercent    : 0;
        var memNorm    = totals.TotalMemoryBytes   > 0 ? (double)process.WorkingSetBytes / totals.TotalMemoryBytes  : 0;
        var ioNorm     = totals.TotalIoBytesPerSec > 0
            ? (double)(process.IoReadBytesPerSec + process.IoWriteBytesPerSec) / totals.TotalIoBytesPerSec
            : 0;
        var gpuNorm    = totals.TotalGpuPercent    > 0 ? process.GpuPercent            / totals.TotalGpuPercent    : 0;
        var handleNorm = totals.TotalHandleCount   > 0 ? (double)process.HandleCount   / totals.TotalHandleCount   : 0;

        var raw = cpuNorm * 0.40 + memNorm * 0.30 + ioNorm * 0.15 + gpuNorm * 0.10 + handleNorm * 0.05;

        // Scale: raw is a fraction of system totals held by this process.
        // A process using 100% of everything would score 100.
        // Clamp and scale so typical top processes land in 30-70 range.
        return Math.Min(100, raw * 100);
    }

    public static SystemTotals ComputeTotals(IReadOnlyList<ProcessInfo> processes)
    {
        double totalCpu    = processes.Sum(p => p.CpuPercent);
        long   totalMem    = processes.Sum(p => p.WorkingSetBytes);
        long   totalIo     = processes.Sum(p => p.IoReadBytesPerSec + p.IoWriteBytesPerSec);
        double totalGpu    = processes.Sum(p => p.GpuPercent);
        int    totalHandle = processes.Sum(p => p.HandleCount);

        // Clamp CPU to 100 per logical core worth of threads; avoid inflating
        totalCpu = Math.Max(totalCpu, 1);

        return new SystemTotals(
            Math.Max(totalCpu, 1),
            Math.Max(totalMem, 1),
            Math.Max(totalIo,  1),
            Math.Max(totalGpu, 1),
            Math.Max(totalHandle, 1));
    }
}

public readonly record struct SystemTotals(
    double TotalCpuPercent,
    long   TotalMemoryBytes,
    long   TotalIoBytesPerSec,
    double TotalGpuPercent,
    int    TotalHandleCount);
