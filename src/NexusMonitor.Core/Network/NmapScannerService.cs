using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;
using System.Reactive.Subjects;

namespace NexusMonitor.Core.Network;

/// <summary>
/// Wraps the nmap CLI binary to perform LAN scans.
/// Exposes progress via <see cref="Progress"/> and final results via <see cref="ScanAsync"/>.
/// </summary>
public sealed class NmapScannerService : IDisposable
{
    private readonly Subject<NmapScanProgress> _progress = new();
    private CancellationTokenSource? _scanCts;

    /// <summary>Stream of scan progress reports. Subscribe before calling <see cref="ScanAsync"/>.</summary>
    public IObservable<NmapScanProgress> Progress => _progress;

    /// <summary>Detects whether nmap is available on PATH.</summary>
    public static bool IsAvailable()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("nmap", "--version")
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            });
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Detects the local subnet from the first non-loopback IPv4 interface.
    /// Returns e.g. "192.168.1.0/24".
    /// </summary>
    public static string DetectLocalSubnet()
    {
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    var ip   = addr.Address;
                    var mask = addr.IPv4Mask;
                    if (mask is null) continue;

                    var ipBytes   = ip.GetAddressBytes();
                    var maskBytes = mask.GetAddressBytes();
                    var netBytes  = new byte[4];
                    for (int i = 0; i < 4; i++) netBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);

                    int prefixLen = 0;
                    foreach (var b in maskBytes)
                        for (int bit = 7; bit >= 0; bit--)
                            if ((b & (1 << bit)) != 0) prefixLen++;

                    return $"{new IPAddress(netBytes)}/{prefixLen}";
                }
            }
        }
        catch { }
        return "192.168.1.0/24";
    }

    /// <summary>
    /// Runs an nmap scan with the given options. Reports progress via <see cref="Progress"/>.
    /// Cancels any in-progress scan before starting a new one.
    /// </summary>
    public async Task<NmapScanResult?> ScanAsync(NmapScanOptions options, CancellationToken ct = default)
    {
        // Cancel any existing scan
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _scanCts.Token;

        var args   = BuildArgs(options);
        var output = new System.Text.StringBuilder();
        var start  = DateTime.UtcNow;

        _progress.OnNext(new NmapScanProgress(0, "Starting scan\u2026", 0));

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("nmap", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                },
                EnableRaisingEvents = true,
            };

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                output.AppendLine(e.Data);
            };

            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                // Parse nmap's stderr stats line: "Stats: 0:00:05 elapsed; 0 hosts completed (5 up), active scanning"
                if (e.Data.Contains("Stats:", StringComparison.OrdinalIgnoreCase))
                {
                    var hostsUp = 0;
                    var m = System.Text.RegularExpressions.Regex.Match(e.Data, @"(\d+) up");
                    if (m.Success) int.TryParse(m.Groups[1].Value, out hostsUp);
                    _progress.OnNext(new NmapScanProgress(
                        PercentDone: -1,
                        StatusText:  e.Data.Trim(),
                        HostsUp:     hostsUp));
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(token);

            if (token.IsCancellationRequested)
            {
                try { proc.Kill(true); } catch { }
                return null;
            }

            var elapsed = DateTime.UtcNow - start;
            var xml     = output.ToString();
            var result  = NmapXmlParser.Parse(xml, elapsed);

            _progress.OnNext(new NmapScanProgress(100, $"Done \u2014 {result.Hosts.Count} hosts up", result.Hosts.Count));
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _progress.OnNext(new NmapScanProgress(0, $"Error: {ex.Message}", 0));
            return null;
        }
    }

    public void CancelScan() => _scanCts?.Cancel();

    private static string BuildArgs(NmapScanOptions o)
    {
        var sb = new System.Text.StringBuilder();

        // Output: XML to stdout
        sb.Append("-oX - ");

        // Stats every 5 seconds via stderr
        sb.Append("--stats-every 5s ");

        // Timing
        sb.Append($"-T{o.TimingTemplate} ");

        // Scan type
        switch (o.ScanType)
        {
            case NmapScanType.PingSweep:
                sb.Append("-sn ");
                break;
            case NmapScanType.FullPorts:
                sb.Append("-p- ");
                break;
            case NmapScanType.ServiceDetection:
                sb.Append("-sV ");
                break;
            case NmapScanType.OsDetection:
                sb.Append("-O ");
                break;
            // DefaultPorts: no extra flags
        }

        if (o.OsDetection    && o.ScanType != NmapScanType.OsDetection)    sb.Append("-O ");
        if (o.ServiceVersion && o.ScanType != NmapScanType.ServiceDetection) sb.Append("-sV ");

        // Target last
        sb.Append(o.Target);

        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _scanCts?.Dispose();
        _progress.OnCompleted();
    }
}
