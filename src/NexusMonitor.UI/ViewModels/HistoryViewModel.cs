using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using System.Linq;
using NexusMonitor.Core.Storage;
using SkiaSharp;

namespace NexusMonitor.UI.ViewModels;

public partial class HistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IMetricsReader _reader;
    private CancellationTokenSource? _loadCts;
    private TimeSpan _currentSpan = TimeSpan.FromHours(24);

    // ── UI state ─────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusText  = "Select a time range to load history.";
    [ObservableProperty] private string _dbInfoText  = string.Empty;
    [ObservableProperty] private bool   _hasData;
    [ObservableProperty] private bool   _is1hActive;
    [ObservableProperty] private bool   _is6hActive;
    [ObservableProperty] private bool   _is24hActive;
    [ObservableProperty] private bool   _is7dActive;
    [ObservableProperty] private bool   _is30dActive;

    // ── Top-process table ────────────────────────────────────────────────────
    public ObservableCollection<ProcessSummary> TopProcesses { get; } = new();

    // ── Events timeline ───────────────────────────────────────────────────────
    public ObservableCollection<StoredEvent> Events { get; } = new();

    [ObservableProperty] private int    _eventCount;
    [ObservableProperty] private string _eventTypeFilter = "All";

    public static IReadOnlyList<string> EventTypeFilters { get; } =
    [
        "All",
        EventType.CpuHigh,
        EventType.MemHigh,
        EventType.GpuHigh,
        EventType.NetAnomaly,
        EventType.NewConnection,
        EventType.ProcessSpike,
    ];

    // ── Chart data collections ───────────────────────────────────────────────
    private readonly ObservableCollection<DateTimePoint> _cpuPts   = new();
    private readonly ObservableCollection<DateTimePoint> _memPts   = new();
    private readonly ObservableCollection<DateTimePoint> _diskRPts = new();
    private readonly ObservableCollection<DateTimePoint> _diskWPts = new();
    private readonly ObservableCollection<DateTimePoint> _netSPts  = new();
    private readonly ObservableCollection<DateTimePoint> _netRPts  = new();
    private readonly ObservableCollection<DateTimePoint> _gpuPts   = new();

    // ── Chart series ─────────────────────────────────────────────────────────
    public ISeries[] CpuSeries  { get; }
    public ISeries[] MemSeries  { get; }
    public ISeries[] DiskSeries { get; }
    public ISeries[] NetSeries  { get; }
    public ISeries[] GpuSeries  { get; }

    // ── Axes (X shared, Y per chart) ─────────────────────────────────────────
    private readonly DateTimeAxis _xAxis;
    public Axis[] XAxes    { get; }
    public Axis[] CpuYAxes  { get; }
    public Axis[] MemYAxes  { get; }
    public Axis[] DiskYAxes { get; }
    public Axis[] NetYAxes  { get; }
    public Axis[] GpuYAxes  { get; }

    public HistoryViewModel(IMetricsReader reader)
    {
        _reader = reader;
        Title   = "History";

        // ── X axis ──────────────────────────────────────────────────────────
        // DateTimeAxis's constructor Labeler = (v) => labeler(new DateTime((long)(v-0.5)))
        // which crashes when LiveChartsCore probes with out-of-range values (0, double.Max…).
        // We replace Labeler with our own safe version that owns the tick→DateTime conversion,
        // applies the same -0.5 offset, range-guards the cast, and reads _currentSpan.
        _xAxis = new DateTimeAxis(TimeSpan.FromMinutes(1), d => d.ToString("HH:mm"))
        {
            TextSize        = 10,
            LabelsPaint     = new SolidColorPaint(new SKColor(120, 120, 120)),
            SeparatorsPaint = new SolidColorPaint(new SKColor(80, 80, 80, 50)),
            TicksPaint      = null,
            SubticksPaint   = null,
        };
        _xAxis.Labeler = value =>
        {
            var ticks = (long)(value - 0.5);
            if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) return string.Empty;
            var dt = new DateTime(ticks);
            if (_currentSpan.TotalHours <= 2)   return dt.ToString("HH:mm:ss");
            if (_currentSpan.TotalDays  <= 1)   return dt.ToString("HH:mm");
            if (_currentSpan.TotalDays  <= 7)   return dt.ToString("ddd HH:mm");
            return dt.ToString("MM/dd");
        };
        XAxes = [_xAxis];

        // ── Series ──────────────────────────────────────────────────────────
        CpuSeries  = [Line(_cpuPts,   new SKColor(10,  132, 255), "CPU %")];
        MemSeries  = [Line(_memPts,   new SKColor(48,  209,  88), "Memory")];
        DiskSeries = [Line(_diskRPts, new SKColor(255, 159,  10), "Read"),
                      Line(_diskWPts, new SKColor(255,  69,  58), "Write")];
        NetSeries  = [Line(_netSPts,  new SKColor(100, 210, 255), "Send"),
                      Line(_netRPts,  new SKColor(191,  90, 242), "Recv")];
        GpuSeries  = [Line(_gpuPts,   new SKColor(255, 214,  10), "GPU %")];

        // ── Y axes ──────────────────────────────────────────────────────────
        CpuYAxes  = [YAxis(0, 100,  v => $"{v:F0}%")];
        MemYAxes  = [YAxis(0, null, v => $"{v:F1} GB")];
        DiskYAxes = [YAxis(0, null, v => $"{v:F2} MB/s")];
        NetYAxes  = [YAxis(0, null, v => $"{v:F2} MB/s")];
        GpuYAxes  = [YAxis(0, 100,  v => $"{v:F0}%")];

        RefreshDbInfo();
    }

    // 4B: Re-load when event type filter changes
    partial void OnEventTypeFilterChanged(string value) => _ = Refresh();

    // ── Range commands ───────────────────────────────────────────────────────
    [RelayCommand] private Task Load1h()  => LoadAsync(TimeSpan.FromHours(1),  is1h:  true);
    [RelayCommand] private Task Load6h()  => LoadAsync(TimeSpan.FromHours(6),  is6h:  true);
    [RelayCommand] private Task Load24h() => LoadAsync(TimeSpan.FromHours(24), is24h: true);
    [RelayCommand] private Task Load7d()  => LoadAsync(TimeSpan.FromDays(7),   is7d:  true);
    [RelayCommand] private Task Load30d() => LoadAsync(TimeSpan.FromDays(30),  is30d: true);

    [RelayCommand]
    private Task Refresh()
    {
        if (Is1hActive)  return LoadAsync(TimeSpan.FromHours(1),  is1h:  true);
        if (Is6hActive)  return LoadAsync(TimeSpan.FromHours(6),  is6h:  true);
        if (Is7dActive)  return LoadAsync(TimeSpan.FromDays(7),   is7d:  true);
        if (Is30dActive) return LoadAsync(TimeSpan.FromDays(30),  is30d: true);
        return                         LoadAsync(TimeSpan.FromHours(24), is24h: true);
    }

    // ── Data loading ─────────────────────────────────────────────────────────
    private async Task LoadAsync(TimeSpan span,
        bool is1h = false, bool is6h = false, bool is24h = false,
        bool is7d = false, bool is30d = false)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        Is1hActive  = is1h;
        Is6hActive  = is6h;
        Is24hActive = is24h;
        Is7dActive  = is7d;
        Is30dActive = is30d;

        IsLoading    = true;
        HasData      = false;
        _currentSpan = span;

        // Tune X axis step width; the formatter closure already reads _currentSpan.
        _xAxis.UnitWidth = span.TotalHours <= 2  ? TimeSpan.FromSeconds(1).Ticks  :
                           span.TotalDays  <= 1  ? TimeSpan.FromMinutes(1).Ticks  :
                           span.TotalDays  <= 7  ? TimeSpan.FromMinutes(5).Ticks  :
                                                   TimeSpan.FromHours(1).Ticks;

        var to   = DateTimeOffset.Now;
        var from = to - span;

        try
        {
            var metrics = await _reader.GetSystemMetricsAsync(from, to, ct);
            var procs   = await _reader.GetTopProcessSummariesAsync(from, to, topN: 10, ct);
            var evtType = EventTypeFilter == "All" ? null : EventTypeFilter;
            var events  = await _reader.GetEventsAsync(from, to, evtType, ct);

            if (ct.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PopulateCharts(metrics);

                TopProcesses.Clear();
                foreach (var p in procs) TopProcesses.Add(p);

                Events.Clear();
                foreach (var e in events.OrderByDescending(e => e.Timestamp)) Events.Add(e);
                EventCount = events.Count;

                HasData    = metrics.Count > 0;
                StatusText = metrics.Count > 0
                    ? $"{metrics.Count:N0} data points · {from.LocalDateTime:g} → {to.LocalDateTime:g}"
                    : "No data for this range. Make sure Metrics is enabled in Settings and the app has been running.";

                RefreshDbInfo();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private void PopulateCharts(IReadOnlyList<MetricsDataPoint> pts)
    {
        static void Fill(ObservableCollection<DateTimePoint> col, IEnumerable<DateTimePoint> src)
        {
            col.Clear();
            foreach (var p in src) col.Add(p);
        }

        Fill(_cpuPts,   pts.Select(p => new DateTimePoint(p.Timestamp.LocalDateTime, p.CpuPercent)));
        Fill(_memPts,   pts.Select(p => new DateTimePoint(p.Timestamp.LocalDateTime, p.MemUsedBytes / 1_073_741_824.0)));
        Fill(_diskRPts, pts.Select(p => new DateTimePoint(p.Timestamp.LocalDateTime, p.DiskReadBps  / 1_048_576.0)));
        Fill(_diskWPts, pts.Select(p => new DateTimePoint(p.Timestamp.LocalDateTime, p.DiskWriteBps / 1_048_576.0)));
        Fill(_netSPts,  pts.Select(p => new DateTimePoint(p.Timestamp.LocalDateTime, p.NetSendBps   / 1_048_576.0)));
        Fill(_netRPts,  pts.Select(p => new DateTimePoint(p.Timestamp.LocalDateTime, p.NetRecvBps   / 1_048_576.0)));
        Fill(_gpuPts,   pts.Select(p => new DateTimePoint(p.Timestamp.LocalDateTime, p.GpuPercent)));
    }

    private void RefreshDbInfo()
    {
        var b = _reader.GetDatabaseSizeBytes();
        DbInfoText = b > 1024 ? $"DB: {b / 1_048_576.0:F1} MB" : string.Empty;
    }

    // ── Factory helpers ──────────────────────────────────────────────────────
    private static LineSeries<DateTimePoint> Line(
        ObservableCollection<DateTimePoint> values, SKColor color, string name) => new()
    {
        Values        = values,
        Name          = name,
        Fill          = new LinearGradientPaint(
            new SKColor(color.Red, color.Green, color.Blue, 45),
            new SKColor(color.Red, color.Green, color.Blue, 0),
            new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f)),
        Stroke        = new SolidColorPaint(color, 1.5f),
        GeometrySize   = 0,
        LineSmoothness = 0.2,
    };

    private static Axis YAxis(double min, double? max, Func<double, string> labeler) => new()
    {
        MinLimit        = min,
        MaxLimit        = max,
        TextSize        = 10,
        LabelsPaint     = new SolidColorPaint(new SKColor(120, 120, 120)),
        SeparatorsPaint = new SolidColorPaint(new SKColor(80, 80, 80, 50)),
        TicksPaint      = null,
        SubticksPaint   = null,
        Labeler         = labeler,
    };

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}
