using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using NexusMonitor.Core.Health;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;
using ReactiveUI;
using SkiaSharp;

namespace NexusMonitor.UI.ViewModels;

public partial class HealthTrendsViewModel : ViewModelBase, IDisposable
{
    private readonly IMetricsReader       _reader;
    private readonly DateTimeAxis         _xAxis;
    private readonly ObservableCollection<DateTimePoint> _pts = new();
    private IDisposable?                  _liveSubscription;

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
        AppSettings settings)
    {
        _reader        = reader;
        MetricsEnabled = settings.MetricsEnabled;

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
        if (!MetricsEnabled)
        {
            SummaryText = "Metrics collection is disabled \u2014 enable it in Settings to see trends.";
            return;
        }

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
            var pts     = await _reader.GetHealthHistoryAsync(from, to);
            var prevPts = await _reader.GetHealthHistoryAsync(from - span, from);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _pts.Clear();
                foreach (var pt in pts)
                    _pts.Add(new DateTimePoint(pt.Timestamp.DateTime, pt.Overall));

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
        catch (Exception ex)
        {
            SummaryText = $"Error loading health data: {ex.Message}";
        }
    }

    // ── Live append ──────────────────────────────────────────────────────────

    private void AppendLive(SystemHealthSnapshot snap)
    {
        // Only live-append for the 24h range; other ranges load on demand
        if (SelectedRange != "24h") return;

        var cutoff = DateTime.UtcNow.AddHours(-24);

        // Prune stale points from the start of the collection
        while (_pts.Count > 0 && _pts[0].DateTime < cutoff)
            _pts.RemoveAt(0);

        _pts.Add(new DateTimePoint(DateTime.UtcNow, snap.OverallScore));
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
        _liveSubscription?.Dispose();
    }
}
