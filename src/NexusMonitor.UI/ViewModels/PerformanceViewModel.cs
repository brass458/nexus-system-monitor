using System.Collections.ObjectModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;
using NexusMonitor.UI.Messages;
using Avalonia.Threading;
using ReactiveUI;

namespace NexusMonitor.UI.ViewModels;

/// <summary>Represents one CPU core cell in the per-core heatmap.</summary>
public record CoreCellViewModel(int Index, double Percent);

/// <summary>Represents one fixed drive in the per-drive disk usage breakdown.</summary>
public record DriveRowViewModel(
    string DriveLetter,
    string Label,
    double TotalGb,
    double UsedGb,
    double FreeGb,
    double UsedPercent);

public partial class PerformanceViewModel : ViewModelBase, IDisposable
{
    private readonly ISystemMetricsProvider _metricsProvider;
    private IDisposable? _subscription;
    private int _initialIntervalMs = 1000;
    private int _ringIdx;
    private const int HistoryLength = 60;

    // CPU
    private readonly ObservableCollection<ObservableValue> _cpuValues = new(Enumerable.Range(0, HistoryLength).Select(_ => new ObservableValue(0)));
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _cpuFrequencyGhz;
    [ObservableProperty] private double _cpuTempC;
    [ObservableProperty] private string _cpuModelName = string.Empty;
    [ObservableProperty] private int _logicalCores;
    [ObservableProperty] private IReadOnlyList<CoreCellViewModel> _coreCells = [];
    public ISeries[] CpuSeries { get; }
    public Axis[] CpuXAxes { get; }
    public Axis[] CpuYAxes { get; }

    // Memory
    private readonly ObservableCollection<ObservableValue> _memValues = new(Enumerable.Range(0, HistoryLength).Select(_ => new ObservableValue(0)));
    [ObservableProperty] private double _memUsedGb;
    [ObservableProperty] private double _memTotalGb;
    [ObservableProperty] private double _memPercent;
    public ISeries[] MemSeries { get; }
    public Axis[] MemYAxes { get; }

    // Disk
    private readonly ObservableCollection<ObservableValue> _diskReadValues = new(Enumerable.Range(0, HistoryLength).Select(_ => new ObservableValue(0)));
    private readonly ObservableCollection<ObservableValue> _diskWriteValues = new(Enumerable.Range(0, HistoryLength).Select(_ => new ObservableValue(0)));
    [ObservableProperty] private double _diskReadMbps;
    [ObservableProperty] private double _diskWriteMbps;
    public ISeries[] DiskSeries { get; }

    // Network
    private readonly ObservableCollection<ObservableValue> _netSendValues = new(Enumerable.Range(0, HistoryLength).Select(_ => new ObservableValue(0)));
    private readonly ObservableCollection<ObservableValue> _netRecvValues = new(Enumerable.Range(0, HistoryLength).Select(_ => new ObservableValue(0)));
    [ObservableProperty] private double _netSendMbps;
    [ObservableProperty] private double _netRecvMbps;
    public ISeries[] NetSeries { get; }

    // Disk drives
    [ObservableProperty] private IReadOnlyList<DriveRowViewModel> _driveRows = [];

    // GPU
    [ObservableProperty] private double _gpuPercent;
    [ObservableProperty] private double _gpuMemGb;
    [ObservableProperty] private double _gpuMemTotalGb;
    [ObservableProperty] private string _gpuName = string.Empty;

    // ── Device sidebar ────────────────────────────────────────────────────────────
    public ObservableCollection<PerfDeviceViewModel> Devices { get; } = new();
    [ObservableProperty] private PerfDeviceViewModel? _selectedDevice;
    private CpuDeviceViewModel?    _cpuDevice;
    private MemoryDeviceViewModel? _memDevice;

    public PerformanceViewModel(ISystemMetricsProvider metricsProvider, SettingsService settings)
    {
        _metricsProvider = metricsProvider;
        Title = "Performance";
        _initialIntervalMs = settings.Current.UpdateIntervalMs;

        var sharedXAxes = new Axis[]
        {
            new() { IsVisible = false, MinLimit = 0, MaxLimit = HistoryLength - 1 }
        };

        CpuSeries =
        [
            new LineSeries<ObservableValue>
            {
                Values = _cpuValues,
                Fill = new LinearGradientPaint(
                    new SKColor(10, 132, 255, 80), new SKColor(10, 132, 255, 0),
                    new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
                Stroke = new SolidColorPaint(new SKColor(10, 132, 255), 2),
                GeometrySize = 0,
                LineSmoothness = 0.6,
                Name = "CPU"
            }
        ];
        CpuXAxes = sharedXAxes;
        CpuYAxes = [new Axis { MinLimit = 0, MaxLimit = 100, IsVisible = false }];

        MemSeries =
        [
            new LineSeries<ObservableValue>
            {
                Values = _memValues,
                Fill = new LinearGradientPaint(
                    new SKColor(52, 199, 89, 80), new SKColor(52, 199, 89, 0),
                    new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
                Stroke = new SolidColorPaint(new SKColor(52, 199, 89), 2),
                GeometrySize = 0,
                LineSmoothness = 0.6,
                Name = "Memory"
            }
        ];
        MemYAxes = [new Axis { MinLimit = 0, MaxLimit = 100, IsVisible = false }];

        DiskSeries =
        [
            new LineSeries<ObservableValue>
            {
                Values = _diskReadValues,
                Stroke = new SolidColorPaint(new SKColor(100, 210, 255), 2),
                Fill = null, GeometrySize = 0, LineSmoothness = 0.6, Name = "Read"
            },
            new LineSeries<ObservableValue>
            {
                Values = _diskWriteValues,
                Stroke = new SolidColorPaint(new SKColor(255, 159, 10), 2),
                Fill = null, GeometrySize = 0, LineSmoothness = 0.6, Name = "Write"
            }
        ];

        NetSeries =
        [
            new LineSeries<ObservableValue>
            {
                Values = _netRecvValues,
                Stroke = new SolidColorPaint(new SKColor(191, 90, 242), 2),
                Fill = null, GeometrySize = 0, LineSmoothness = 0.6, Name = "Download"
            },
            new LineSeries<ObservableValue>
            {
                Values = _netSendValues,
                Stroke = new SolidColorPaint(new SKColor(255, 69, 58), 2),
                Fill = null, GeometrySize = 0, LineSmoothness = 0.6, Name = "Upload"
            }
        ];

        StartMetricsStream(TimeSpan.FromMilliseconds(_initialIntervalMs));

        WeakReferenceMessenger.Default.Register<MetricsIntervalChangedMessage>(this, (_, msg) =>
            Dispatcher.UIThread.InvokeAsync(() => StartMetricsStream(msg.Interval)));

        // Populate sidebar devices
        var overview = new OverviewDeviceViewModel();
        _cpuDevice   = new CpuDeviceViewModel(string.Empty);
        _memDevice   = new MemoryDeviceViewModel();
        Devices.Add(overview);
        Devices.Add(_cpuDevice);
        Devices.Add(_memDevice);
        SelectedDevice = overview;
        overview.IsSelected = true;
    }

    partial void OnSelectedDeviceChanged(PerfDeviceViewModel? oldValue, PerfDeviceViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    // Already on UI thread via ObserveOn(RxApp.MainThreadScheduler) — no inner Post needed.
    private void Update(SystemMetrics m)
    {
        // CPU
        CpuPercent      = Math.Round(m.Cpu.TotalPercent, 1);
        CpuFrequencyGhz = Math.Round(m.Cpu.FrequencyMhz / 1000.0, 2);
        CpuTempC        = Math.Round(m.Cpu.TemperatureCelsius, 0);
        CpuModelName    = m.Cpu.ModelName;
        LogicalCores    = m.Cpu.LogicalCores;
        Push(_cpuValues, _ringIdx, m.Cpu.TotalPercent);

        // Per-core heatmap cells — update in-place to avoid allocating a new list every tick
        if (m.Cpu.CorePercents.Count > 0)
        {
            var corePercents = m.Cpu.CorePercents;
            var existing     = CoreCells;
            if (existing.Count == corePercents.Count)
            {
                // Update in-place: replace only changed cells (record with{} allocates only for changed)
                bool changed = false;
                var updated = new CoreCellViewModel[corePercents.Count];
                for (int ci = 0; ci < corePercents.Count; ci++)
                {
                    var rounded = Math.Round(corePercents[ci], 0);
                    updated[ci] = existing[ci] with { Percent = rounded };
                    if (updated[ci].Percent != existing[ci].Percent) changed = true;
                }
                if (changed) CoreCells = updated;
            }
            else
            {
                CoreCells = [.. corePercents.Select((p, i) => new CoreCellViewModel(i, Math.Round(p, 0)))];
            }
        }

        // Memory
        MemTotalGb = Math.Round(m.Memory.TotalBytes / 1e9, 1);
        MemUsedGb  = Math.Round(m.Memory.UsedBytes  / 1e9, 1);
        MemPercent = Math.Round(m.Memory.UsedPercent, 1);
        Push(_memValues, _ringIdx, m.Memory.UsedPercent);

        // Disk (aggregate first disk)
        if (m.Disks.Count > 0)
        {
            DiskReadMbps  = Math.Round(m.Disks[0].ReadBytesPerSec  / 1e6, 1);
            DiskWriteMbps = Math.Round(m.Disks[0].WriteBytesPerSec / 1e6, 1);
            Push(_diskReadValues,  _ringIdx, m.Disks[0].ReadBytesPerSec  / 1e6);
            Push(_diskWriteValues, _ringIdx, m.Disks[0].WriteBytesPerSec / 1e6);
        }

        // Per-drive summary — always update so stale rows are cleared when Disks is empty
        DriveRows = m.Disks
            .Where(d => d.TotalBytes > 0)
            .Select(d =>
            {
                double totalGb = Math.Round(d.TotalBytes / 1e9, 1);
                double freeGb  = Math.Round(d.FreeBytes  / 1e9, 1);
                double usedGb  = Math.Round(totalGb - freeGb, 1);
                double pct     = totalGb > 0 ? Math.Round((usedGb / totalGb) * 100, 0) : 0;
                return new DriveRowViewModel(d.DriveLetter, d.Label, totalGb, usedGb, freeGb, pct);
            })
            .ToList();

        // Network — prefer the adapter with the most activity (active NIC)
        if (m.NetworkAdapters.Count > 0)
        {
            var activeNic = m.NetworkAdapters
                .OrderByDescending(a => a.SendBytesPerSec + a.RecvBytesPerSec)
                .ThenByDescending(a => a.IsConnected ? 1 : 0)
                .First();
            NetSendMbps = Math.Round(activeNic.SendBytesPerSec / 1e6, 2);
            NetRecvMbps = Math.Round(activeNic.RecvBytesPerSec / 1e6, 2);
            Push(_netSendValues, _ringIdx, activeNic.SendBytesPerSec / 1e6);
            Push(_netRecvValues, _ringIdx, activeNic.RecvBytesPerSec / 1e6);
        }

        _ringIdx++;

        // GPU
        if (m.Gpus.Count > 0)
        {
            GpuPercent    = Math.Round(m.Gpus[0].UsagePercent, 1);
            GpuMemGb      = Math.Round(m.Gpus[0].DedicatedMemoryUsedBytes   / 1e9, 1);
            GpuMemTotalGb = Math.Round(m.Gpus[0].DedicatedMemoryTotalBytes  / 1e9, 1);
            GpuName       = m.Gpus[0].Name;
        }

        // Update CPU device name when model is known
        if (_cpuDevice is not null && m.Cpu.ModelName.Length > 0)
            _cpuDevice.SetModelName(m.Cpu.ModelName);

        // Update all device VMs
        foreach (var dev in Devices)
            dev.Update(m);

        // Sync dynamic devices (add new, remove stale)
        SyncDiskDevices(m.Disks);
        SyncNetworkDevices(m.NetworkAdapters);
        SyncGpuDevices(m.Gpus);
    }

    private static void Push(ObservableCollection<ObservableValue> col, int ringIdx, double value)
        => col[ringIdx % col.Count].Value = value;

    private void SyncDiskDevices(IReadOnlyList<DiskMetrics> disks)
    {
        var activeIndexes = new HashSet<int>(disks.Select(d => d.DiskIndex));
        // Add new
        foreach (var d in disks)
            if (!Devices.OfType<DiskDeviceViewModel>().Any(x => x.DiskIndex == d.DiskIndex))
                Devices.Add(new DiskDeviceViewModel(d));
        // Remove stale
        foreach (var dev in Devices.OfType<DiskDeviceViewModel>()
                                    .Where(x => !activeIndexes.Contains(x.DiskIndex))
                                    .ToList())
            Devices.Remove(dev);
    }

    private void SyncNetworkDevices(IReadOnlyList<NetworkAdapterMetrics> adapters)
    {
        var activeKeys = new HashSet<string>(adapters.Select(n => n.Name.Length > 0 ? n.Name : n.Description));
        // Sort: connected / highest-throughput adapters first in the sidebar
        var sorted = adapters
            .OrderByDescending(a => a.IsConnected ? 1 : 0)
            .ThenByDescending(a => a.SendBytesPerSec + a.RecvBytesPerSec);
        foreach (var n in sorted)
        {
            string key = n.Name.Length > 0 ? n.Name : n.Description;
            if (!Devices.OfType<NetworkDeviceViewModel>().Any(x => x.AdapterName == key))
                Devices.Add(new NetworkDeviceViewModel(n));
        }
        foreach (var dev in Devices.OfType<NetworkDeviceViewModel>()
                                    .Where(x => !activeKeys.Contains(x.AdapterName))
                                    .ToList())
            Devices.Remove(dev);
    }

    private void SyncGpuDevices(IReadOnlyList<GpuMetrics> gpus)
    {
        var activeNames = new HashSet<string>(gpus.Select(g => g.Name));
        foreach (var g in gpus)
            if (!Devices.OfType<GpuDeviceViewModel>().Any(x => x.DeviceName == g.Name))
                Devices.Add(new GpuDeviceViewModel(g));
        foreach (var dev in Devices.OfType<GpuDeviceViewModel>()
                                    .Where(x => !activeNames.Contains(x.DeviceName))
                                    .ToList())
            Devices.Remove(dev);
    }

    private void StartMetricsStream(TimeSpan interval)
    {
        _subscription?.Dispose();
        _subscription = _metricsProvider
            .GetMetricsStream(interval)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Update);
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _subscription?.Dispose();
    }
}
