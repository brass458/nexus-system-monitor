namespace NexusMonitor.Core.Network;

/// <summary>Scan type presets passed to nmap.</summary>
public enum NmapScanType
{
    /// <summary>Quick ping scan: fast, no port scan.</summary>
    PingSweep,
    /// <summary>Default port scan: top 1000 ports.</summary>
    DefaultPorts,
    /// <summary>Full port scan: all 65535 ports (slow).</summary>
    FullPorts,
    /// <summary>Service and version detection.</summary>
    ServiceDetection,
    /// <summary>OS fingerprinting (requires root/admin).</summary>
    OsDetection,
}

/// <summary>Options passed to <see cref="NmapScannerService"/>.</summary>
public sealed record NmapScanOptions(
    string     Target,
    NmapScanType ScanType       = NmapScanType.DefaultPorts,
    bool       OsDetection     = false,
    bool       ServiceVersion  = false,
    int        TimingTemplate  = 3    // -T3 (normal)
);

/// <summary>Progress report emitted during a scan.</summary>
public sealed record NmapScanProgress(
    double PercentDone,
    string StatusText,
    int    HostsUp);

/// <summary>A single open or detected port on a host.</summary>
public sealed record NmapPort(
    int    Number,
    string Protocol,
    string State,
    string Service,
    string Version);

/// <summary>A discovered host from an nmap scan.</summary>
public sealed record NmapHost(
    string              IpAddress,
    string              Hostname,
    string              MacAddress,
    string              OsGuess,
    double              Latency,
    IReadOnlyList<NmapPort> Ports);

/// <summary>Completed scan result.</summary>
public sealed record NmapScanResult(
    IReadOnlyList<NmapHost> Hosts,
    TimeSpan                Elapsed,
    string                  RawXml);
