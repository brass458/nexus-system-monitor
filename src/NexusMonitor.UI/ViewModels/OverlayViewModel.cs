using System.Collections.ObjectModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;
using NexusMonitor.UI.Messages;
using Avalonia.Threading;
using ReactiveUI;
using SkiaSharp;

namespace NexusMonitor.UI.ViewModels;

public partial class OverlayViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _cpuDisplay     = "0%";
    [ObservableProperty] private double _cpuPercent     = 0;
    [ObservableProperty] private string _memDisplay     = "0 / 0 GB";
    [ObservableProperty] private double _memPercent     = 0;
    [ObservableProperty] private string _netSendDisplay = "↑ 0 B/s";
    [ObservableProperty] private string _netRecvDisplay = "↓ 0 B/s";
    [ObservableProperty] private string _gpuDisplay     = "0%";
    [ObservableProperty] private bool   _hasGpu;

    public ObservableCollection<ObservableValue> CpuHistory { get; } =
        new(Enumerable.Range(0, 30).Select(_ => new ObservableValue(0)));

    public ISeries[] CpuSeries { get; }
    public Axis[]    CpuXAxes  { get; } = [new() { IsVisible = false, MinLimit = 0, MaxLimit = 29 }];
    public Axis[]    CpuYAxes  { get; } = [new() { IsVisible = false, MinLimit = 0, MaxLimit = 100 }];

    private readonly ISystemMetricsProvider _provider;
    private IDisposable? _sub;
    private int _cpuRingIdx;

    public OverlayViewModel(ISystemMetricsProvider provider, SettingsService settings)
    {
        _provider = provider;

        CpuSeries =
        [
            new LineSeries<ObservableValue>
            {
                Values          = CpuHistory,
                GeometrySize    = 0,
                LineSmoothness  = 0.3,
                AnimationsSpeed = TimeSpan.Zero,
                Stroke = new SolidColorPaint(new SKColor(0, 255, 255, 200), 1.5f),
                Fill   = new LinearGradientPaint(
                    new[] { new SKColor(0, 255, 255, 80), new SKColor(0, 255, 255, 0) },
                    new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
            }
        ];

        StartStream(TimeSpan.FromMilliseconds(settings.Current.UpdateIntervalMs));

        WeakReferenceMessenger.Default.Register<MetricsIntervalChangedMessage>(this, (_, msg) =>
            Dispatcher.UIThread.InvokeAsync(() => StartStream(msg.Interval)));
    }

    private void StartStream(TimeSpan interval)
    {
        _sub?.Dispose();
        _sub = _provider.GetMetricsStream(interval)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Update);
    }

    private void Update(SystemMetrics m)
    {
        CpuPercent  = m.Cpu.TotalPercent;
        CpuDisplay  = $"{m.Cpu.TotalPercent:F0}%";
        MemPercent  = m.Memory.TotalBytes > 0
            ? (double)m.Memory.UsedBytes / m.Memory.TotalBytes * 100
            : 0;
        MemDisplay  = $"{m.Memory.UsedBytes / 1e9:F1} / {m.Memory.TotalBytes / 1e9:F1} GB";

        var net = m.NetworkAdapters.FirstOrDefault();
        if (net is not null)
        {
            NetSendDisplay = $"↑ {FmtRate(net.SendBytesPerSec)}";
            NetRecvDisplay = $"↓ {FmtRate(net.RecvBytesPerSec)}";
        }

        var gpu = m.Gpus.FirstOrDefault();
        HasGpu = gpu is not null;
        if (gpu is not null) GpuDisplay = $"{gpu.UsagePercent:F0}%";

        CpuHistory[_cpuRingIdx % CpuHistory.Count].Value = m.Cpu.TotalPercent;
        _cpuRingIdx++;
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _sub?.Dispose();
    }

    private static string FmtRate(long bps) => bps switch
    {
        >= 1_000_000 => $"{bps / 1_000_000.0:F1} MB/s",
        >= 1_000     => $"{bps / 1_000.0:F0} KB/s",
        _            => $"{bps} B/s",
    };
}
