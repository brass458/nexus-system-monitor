using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using NexusMonitor.Core.Health;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;
using NexusMonitor.UI.Messages;
using ReactiveUI;
using SkiaSharp;

namespace NexusMonitor.UI.ViewModels;

public partial class HealthTrendsViewModel : ViewModelBase, IDisposable
{
    private readonly IMetricsReader       _reader;
    private readonly ILogger<HealthTrendsViewModel> _logger;
    private readonly DateTimeAxis         _xAxis;
    private readonly ObservableCollection<DateTimePoint> _pts = new();
    private IDisposable?                  _liveSubscription;
    private CancellationTokenSource?      _loadCts;

    [ObservableProperty] private string _selectedRange  = "24h";
    [ObservableProperty] private string _summaryText    = "Loading\u2026";
    [ObservableProperty] private bool   _metricsEnabled;

    public ISeries[] HealthSeries { get; }
    public Axis[]    XAxes        { get; }
    public Axis[]    YAxes        { get; }

    public static IReadOnlyList<string> RangeOptions { get; } = ["24h", "7d", "30d"];

    public HealthTrendsViewModel(
        IMetricsReader reader,
        SystemHealthService healthService,
        AppSettings settings,
        ILogger<HealthTrendsViewModel> logger)
    {
        _reader        = reader;
        _logger        = logger;
        MetricsEnabled = settings.MetricsEnabled;

        WeakReferenceMessenger.Default.Register<MetricsEnabledChangedMessage>(this, (_, msg) =>
            Dispatcher.UIThread.Post(() => MetricsEnabled = msg.Enabled));

        // ── X axis with crash-guard labeler ──────────────────────────────────
        // DateTimeAxis's constructor Labeler = (v) => labeler(new DateTime((long)(v-0.5)))
        // which crashes when LiveChartsCore probes with out-of-range values (0, double.Max…).
        // We replace Labeler with our own safe version that owns the tick→DateTime conversion,
        // applies the same -0.5 offset, range-guards the cast, and reads _selectedRange.
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
            if (SelectedRange == "24h") return dt.ToString("HH:mm");
            if (SelectedRange == "7d")  return dt.ToString("ddd HH:mm");
            return dt.ToString("MM/dd");
        };
        XAxes = [_xAxis];

        // ── Series ───────────────────────────────────────────────────────────
        HealthSeries = [Line(_pts, new SKColor(48, 209, 88), "Health Score")];

        // ── Y axis (0–100) ───────────────────────────────────────────────────
        YAxes = [YAxis(0, 100, v => $"{v:F0}")];

        // ── Live subscription ────────────────────────────────────────────────
        _liveSubscription = healthService.HealthStream
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(snap => AppendLive(snap));

        _ = LoadAsync();
    }

    // Triggered when user changes the range ComboBox selection
    partial void OnSelectedRangeChanged(string value) => _ = LoadAsync();

    // ── Data loading ─────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        var span = SelectedRange switch
        {
            "7d"  => TimeSpan.FromDays(7),
            "30d" => TimeSpan.FromDays(30),
            _     => TimeSpan.FromHours(24),
        };

        var to   = DateTimeOffset.UtcNow;
        var from = to - span;

        try
        {
            if (!MetricsEnabled)
            {
                SummaryText = "Metrics collection is disabled \u2014 enable it in Settings to see trends.";
                return;
            }

            var pts     = await _reader.GetHealthHistoryAsync(from, to, ct);
            var prevPts = await _reader.GetHealthHistoryAsync(from - span, from, ct);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _pts.Clear();
                int step = Math.Max(1, pts.Count / 2000);
                for (int i = 0; i < pts.Count; i += step)
                    _pts.Add(new DateTimePoint(pts[i].Timestamp.DateTime, pts[i].Overall));

                if (_pts.Count == 0)
                {
                    SummaryText = "No health data yet \u2014 enable metrics collection in Settings.";
                    return;
                }

                var avg     = pts.Average(p => p.Overall);
                var prevAvg = prevPts.Count > 0 ? prevPts.Average(p => p.Overall) : avg;
                var delta   = avg - prevAvg;
                var sign    = delta > 0 ? "+" : string.Empty;
                SummaryText = $"Avg health: {avg:F0}  |  vs prior period: {sign}{delta:F0}";
            });
        }
        catch (OperationCanceledException)
        {
            // stale load cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load health history");
            SummaryText = "Failed to load health data.";
        }
    }

    // ── Live append ──────────────────────────────────────────────────────────

    private void AppendLive(SystemHealthSnapshot snapshot)
    {
        if (SelectedRange != "24h") return;
        // Already on the UI thread via .ObserveOn(RxApp.MainThreadScheduler) in the subscription
        _pts.Add(new DateTimePoint(DateTime.UtcNow, snapshot.OverallScore));
        // Trim in batches to avoid O(n) RemoveAt on every tick
        if (_pts.Count > 2200)
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            while (_pts.Count > 0 && _pts[0].DateTime < cutoff)
                _pts.RemoveAt(0);
        }
    }

    // ── Factory helpers ──────────────────────────────────────────────────────

    private static LineSeries<DateTimePoint> Line(
        ObservableCollection<DateTimePoint> pts, SKColor color, string name) =>
        new LineSeries<DateTimePoint>
        {
            Values          = pts,
            Name            = name,
            Stroke          = new SolidColorPaint(color) { StrokeThickness = 2 },
            Fill            = null,
            GeometrySize    = 0,
            GeometryFill    = null,
            GeometryStroke  = null,
            LineSmoothness  = 0,
            AnimationsSpeed = TimeSpan.Zero,
        };

    private static Axis YAxis(double min, double? max, Func<double, string> labeler) =>
        new Axis
        {
            MinLimit        = min,
            MaxLimit        = max,
            Labeler         = labeler,
            TextSize        = 10,
            LabelsPaint     = new SolidColorPaint(new SKColor(120, 120, 120)),
            SeparatorsPaint = new SolidColorPaint(new SKColor(80, 80, 80, 50)),
            TicksPaint      = null,
            SubticksPaint   = null,
        };

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _liveSubscription?.Dispose();
        _loadCts?.Dispose();
    }
}
