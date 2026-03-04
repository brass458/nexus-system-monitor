using System.Collections.ObjectModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Network;
using ReactiveUI;

namespace NexusMonitor.UI.ViewModels;

public partial class LanScannerViewModel : ViewModelBase, IDisposable
{
    private readonly NmapScannerService _scanner;
    private IDisposable? _progressSub;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string  _target       = NmapScannerService.DetectLocalSubnet();
    [ObservableProperty] private int     _scanTypeIndex = 1; // DefaultPorts
    [ObservableProperty] private bool    _osDetection   = false;
    [ObservableProperty] private bool    _serviceVersion = false;
    [ObservableProperty] private bool    _isScanning    = false;
    [ObservableProperty] private string  _statusText    = "Ready";
    [ObservableProperty] private int     _hostsUp       = 0;
    [ObservableProperty] private double  _progress      = 0;
    [ObservableProperty] private bool    _nmapAvailable = false;
    [ObservableProperty] private NmapHost? _selectedHost;

    public ObservableCollection<NmapHost> Hosts { get; } = [];

    public static IReadOnlyList<string> ScanTypeLabels { get; } =
        ["Ping Sweep", "Default Ports", "Full Ports", "Service Detection", "OS Detection"];

    public LanScannerViewModel(NmapScannerService scanner)
    {
        Title    = "LAN Scanner";
        _scanner = scanner;

        // Check nmap availability on background thread to avoid blocking startup
        _ = Task.Run(() =>
        {
            NmapAvailable = NmapScannerService.IsAvailable();
            if (!NmapAvailable)
                StatusText = "nmap not found \u2014 install nmap to use this feature";
        });
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task Scan()
    {
        if (!NmapAvailable) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        Hosts.Clear();
        SelectedHost = null;
        IsScanning   = true;
        Progress     = 0;
        HostsUp      = 0;

        // Subscribe to progress
        _progressSub?.Dispose();
        _progressSub = _scanner.Progress
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(p =>
            {
                StatusText = p.StatusText;
                HostsUp    = p.HostsUp;
                if (p.PercentDone >= 0) Progress = p.PercentDone;
            });

        var options = new NmapScanOptions(
            Target:         Target,
            ScanType:       (NmapScanType)ScanTypeIndex,
            OsDetection:    OsDetection,
            ServiceVersion: ServiceVersion);

        try
        {
            var result = await _scanner.ScanAsync(options, _cts.Token);
            if (result is not null)
            {
                foreach (var host in result.Hosts.OrderBy(h => ParseIp(h.IpAddress)))
                    Hosts.Add(host);
                StatusText = $"Scan complete \u2014 {result.Hosts.Count} hosts up in {result.Elapsed.TotalSeconds:F1}s";
                HostsUp    = result.Hosts.Count;
                Progress   = 100;
            }
            else
            {
                StatusText = "Scan cancelled";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _progressSub?.Dispose();
            _progressSub = null;
        }
    }

    private bool CanScan() => NmapAvailable && !IsScanning;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private static long ParseIp(string ip)
    {
        try
        {
            var parts = ip.Split('.');
            if (parts.Length != 4) return 0;
            return (long.Parse(parts[0]) << 24) | (long.Parse(parts[1]) << 16) |
                   (long.Parse(parts[2]) << 8)  | long.Parse(parts[3]);
        }
        catch { return 0; }
    }

    partial void OnIsScanningChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();
    partial void OnNmapAvailableChanged(bool value) => ScanCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        _progressSub?.Dispose();
        _cts?.Dispose();
    }
}
