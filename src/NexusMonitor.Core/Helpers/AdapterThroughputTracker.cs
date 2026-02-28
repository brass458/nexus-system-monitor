using System.Net.NetworkInformation;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Helpers;

/// <summary>
/// Tracks cumulative adapter byte counters and computes bytes-per-second rates.
/// Uses <c>System.Net.NetworkInformation.NetworkInterface</c> — no elevation needed.
/// Not thread-safe; intended to be called from a single reactive stream.
/// </summary>
public sealed class AdapterThroughputTracker
{
    private long _prevSent, _prevRecv, _prevTicks;

    public AdapterThroughput Sample()
    {
        try
        {
            long sent = 0, recv = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = ni.GetIPStatistics();
                sent += stats.BytesSent;
                recv += stats.BytesReceived;
            }

            long now = DateTime.UtcNow.Ticks;
            long sendRate = 0, recvRate = 0;

            if (_prevTicks > 0)
            {
                double elapsed = (now - _prevTicks) / (double)TimeSpan.TicksPerSecond;
                if (elapsed >= 0.1 && sent >= _prevSent && recv >= _prevRecv)
                {
                    sendRate = (long)((sent - _prevSent) / elapsed);
                    recvRate = (long)((recv - _prevRecv) / elapsed);
                }
            }

            _prevSent  = sent;
            _prevRecv  = recv;
            _prevTicks = now;

            return new AdapterThroughput(sendRate, recvRate);
        }
        catch
        {
            return AdapterThroughput.Zero;
        }
    }
}
