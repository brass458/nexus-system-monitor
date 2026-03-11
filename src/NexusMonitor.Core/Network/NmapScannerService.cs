using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;

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

    /// <summary>Detects whether nmap is available, checking well-known install paths first,
    /// then re-reading PATH from the registry so newly installed nmap is found without
    /// restarting the app.</summary>
    public static bool IsAvailable()
    {
        // Check well-known install paths first — handles newly installed nmap before PATH updates
        string[] fallbackPaths = OperatingSystem.IsWindows()
            ? [@"C:\Program Files (x86)\Nmap\nmap.exe", @"C:\Program Files\Nmap\nmap.exe"]
            : ["/usr/bin/nmap", "/usr/local/bin/nmap", "/opt/homebrew/bin/nmap"];

        foreach (var path in fallbackPaths)
            if (File.Exists(path)) return true;

        try
        {
            var psi = new ProcessStartInfo("nmap", "--version")
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            // On Windows, refresh PATH from registry so newly installed tools are detected
            // without requiring the app to restart.
            if (OperatingSystem.IsWindows())
            {
                var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
                var userPath    = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
                psi.Environment["PATH"] = machinePath + ";" + userPath;
            }

            using var proc = Process.Start(psi);
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

                // Line like: "SYN Stealth Scan Timing: About 45.00% done; ETC: ..."
                if (e.Data.Contains("% done", StringComparison.OrdinalIgnoreCase))
                {
                    var m = Regex.Match(e.Data, @"About\s+([\d.]+)%\s+done");
                    if (m.Success && double.TryParse(m.Groups[1].Value,
                        NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
                        _progress.OnNext(new NmapScanProgress(pct, e.Data.Trim(), -1));
                }
                // Line like: "Stats: 0:00:05 elapsed; 0 hosts completed (5 up), ..."
                else if (e.Data.Contains("Stats:", StringComparison.OrdinalIgnoreCase))
                {
                    var hostsUp = 0;
                    var m = Regex.Match(e.Data, @"(\d+) up");
                    if (m.Success) int.TryParse(m.Groups[1].Value, out hostsUp);
                    _progress.OnNext(new NmapScanProgress(-1, e.Data.Trim(), hostsUp));
                }
                // Anything else on stderr: surface to user (e.g. privilege errors)
                else if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _progress.OnNext(new NmapScanProgress(-1, e.Data.Trim(), -1));
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

    // ── Install helpers ────────────────────────────────────────────────────────

    /// <summary>Platform-specific package manager install details.</summary>
    public record NmapInstallInfo(string Executable, string Arguments, string PackageManagerName);

    /// <summary>
    /// Returns install info for the current platform, or <c>null</c> if no
    /// supported package manager is detected.
    /// </summary>
    public static NmapInstallInfo? GetInstallInfo()
    {
        if (OperatingSystem.IsWindows())
            return new NmapInstallInfo(
                "winget",
                "install Insecure.Nmap --accept-package-agreements --accept-source-agreements",
                "winget");

        if (OperatingSystem.IsMacOS())
            return new NmapInstallInfo("brew", "install nmap", "brew");

        if (OperatingSystem.IsLinux())
        {
            if (File.Exists("/usr/bin/apt-get"))
                return new NmapInstallInfo("pkexec", "apt-get install -y nmap", "apt");
            if (File.Exists("/usr/bin/pacman"))
                return new NmapInstallInfo("pkexec", "pacman -S --noconfirm nmap", "pacman");
            if (File.Exists("/usr/bin/dnf"))
                return new NmapInstallInfo("pkexec", "dnf install -y nmap", "dnf");
            if (File.Exists("/usr/bin/zypper"))
                return new NmapInstallInfo("pkexec", "zypper install -y nmap", "zypper");
            if (File.Exists("/sbin/apk"))
                return new NmapInstallInfo("pkexec", "apk add nmap", "apk");
        }

        return null;
    }

    /// <summary>Returns the detected package manager name, or an empty string if none found.</summary>
    public static string GetPackageManagerName() => GetInstallInfo()?.PackageManagerName ?? "";

    /// <summary>
    /// Installs nmap via the detected package manager.
    /// Each output line is reported via <paramref name="progress"/>.
    /// Returns <c>(success, full output)</c>.
    /// </summary>
    public static async Task<(bool Success, string Output)> InstallAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var info = GetInstallInfo();
        if (info is null)
            return (false, "No supported package manager found on this system.");

        var output = new System.Text.StringBuilder();

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo(info.Executable, info.Arguments)
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
                progress?.Report(e.Data);
            };

            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                output.AppendLine(e.Data);
                progress?.Report(e.Data);
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);

            return (proc.ExitCode == 0, output.ToString());
        }
        catch (OperationCanceledException)
        {
            return (false, "Installation cancelled.");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}\n{output}");
        }
    }

    /// <summary>
    /// Validates that the target is a safe IPv4, IPv6, CIDR, or hostname — no embedded flags.
    /// Allows: single IPs, CIDR ranges (192.168.1.0/24), hostnames, nmap range syntax (10.0.0.1-10),
    /// and space-separated combinations of the above.
    /// </summary>
    private static readonly Regex _targetTokenPattern = new(
        @"^[A-Za-z0-9.\-:/\[\]]{1,253}$",
        RegexOptions.Compiled);

    private static string SanitizeTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Scan target must not be empty.");

        // Split on whitespace — nmap accepts multiple targets separated by spaces
        var tokens = target.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (!_targetTokenPattern.IsMatch(token))
                throw new ArgumentException($"Invalid scan target token: '{token}'. Only IPs, CIDR ranges, and hostnames are allowed.");
        }

        return string.Join(' ', tokens);
    }

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

        // Target last — validated to prevent flag injection
        sb.Append(SanitizeTarget(o.Target));

        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _scanCts?.Dispose();
        _progress.OnCompleted();
    }
}
