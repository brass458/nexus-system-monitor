namespace NexusMonitor.Core.Models;

/// <summary>
/// Aggregate network adapter throughput (sum of all non-loopback active adapters).
/// Populated via <c>System.Net.NetworkInformation.NetworkInterface</c> — no elevation required.
/// </summary>
public record AdapterThroughput(long SendBytesPerSec, long RecvBytesPerSec)
{
    public string SendDisplay => FormatRate(SendBytesPerSec);
    public string RecvDisplay => FormatRate(RecvBytesPerSec);

    public static readonly AdapterThroughput Zero = new(0, 0);

    private static string FormatRate(long bps) => bps switch
    {
        >= 1_048_576 => $"{bps / 1_048_576.0:F1} MB/s",
        >= 1_024     => $"{bps / 1_024.0:F0} KB/s",
        > 0          => $"{bps} B/s",
        _            => "—",
    };
}
