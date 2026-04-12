using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Microsoft.Extensions.Logging;
using NexusMonitor.Core.Automation;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;

namespace NexusMonitor.Core.Health;

/// <summary>
/// Periodically analyses health score history and emits forward-looking
/// <see cref="ResourcePrediction"/> items for resources that show a
/// statistically significant declining trend.
/// </summary>
public sealed class PredictionService : IDisposable
{
    // ── Dependencies ───────────────────────────────────────────────────────
    private readonly IMetricsReader                  _metricsReader;
    private readonly AppSettings                     _settings;
    private readonly ILogger<PredictionService>      _logger;
    private readonly QuietHoursService?              _quietHours;
    private readonly Func<DateTimeOffset>             _clock;

    // ── State ──────────────────────────────────────────────────────────────
    private readonly SemaphoreSlim                                           _tickLock    = new(1, 1);
    private readonly BehaviorSubject<IReadOnlyList<ResourcePrediction>>      _predictions = new(Array.Empty<ResourcePrediction>());
    private Timer?  _timer;
    private volatile bool _running;
    private int _started; // 0 = not started, 1 = started — guarded by Interlocked

    // Fallback sampling rate when only 1 data point exists (never used in practice since MinDataPoints>1)
    private const double DefaultSamplesPerHour = 3600.0 / 30.0;

    // R² threshold: require a reasonably good linear fit before acting
    private const double RSquaredThreshold = 0.5;

    // Minimum data points before we try to predict anything
    private const int MinDataPoints = 10;

    // ── Public surface ─────────────────────────────────────────────────────

    public IObservable<IReadOnlyList<ResourcePrediction>> Predictions => _predictions.AsObservable();

    // ── Constructors ───────────────────────────────────────────────────────

    /// <summary>Production constructor — uses <see cref="DateTime.UtcNow"/>.</summary>
    public PredictionService(
        IMetricsReader             metricsReader,
        AppSettings                settings,
        ILogger<PredictionService> logger,
        QuietHoursService?         quietHours = null)
        : this(metricsReader, settings, logger, quietHours, () => DateTimeOffset.UtcNow) { }

    /// <summary>Testable constructor — accepts clock injection.</summary>
    public PredictionService(
        IMetricsReader             metricsReader,
        AppSettings                settings,
        ILogger<PredictionService> logger,
        QuietHoursService?         quietHours,
        Func<DateTimeOffset>       clock)
    {
        _metricsReader = metricsReader;
        _settings      = settings;
        _logger        = logger;
        _quietHours    = quietHours;
        _clock         = clock;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        _running = true;
        // Fire once immediately, then every 5 minutes; re-arm happens at end of RunPredictionsAsync
        _timer = new Timer(_ => _ = RunPredictionsAsync(), null,
            TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    public void Stop()
    {
        if (Interlocked.CompareExchange(ref _started, 0, 1) != 1) return;
        _running = false;
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        Stop();
        _tickLock.Dispose();
        _predictions.Dispose();
    }

    // ── Core logic ─────────────────────────────────────────────────────────

    /// <summary>
    /// Queries health history for the last 24 hours and emits predictions
    /// for any resources with a statistically significant declining trend.
    /// Made <c>internal</c> to allow direct invocation from tests.
    /// </summary>
    internal async Task RunPredictionsAsync()
    {
        // Skip-if-busy pattern
        if (!await _tickLock.WaitAsync(0)) return;
        try
        {
            await ComputeAndEmitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PredictionService.RunPredictionsAsync failed");
        }
        finally
        {
            _tickLock.Release();

            // Re-arm the timer for the next 5-minute cycle (only when still running)
            if (_running)
            {
                try { _timer?.Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { /* service was disposed mid-tick */ }
            }
        }
    }

    private async Task ComputeAndEmitAsync()
    {
        if (!_settings.PredictionsEnabled)
        {
            _predictions.OnNext(Array.Empty<ResourcePrediction>());
            return;
        }

        var now  = _clock();
        var from = now.AddHours(-24);

        IReadOnlyList<HealthDataPoint> points;
        try
        {
            points = await _metricsReader.GetHealthHistoryAsync(from, now);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PredictionService: failed to read health history");
            return;
        }

        if (points.Count < MinDataPoints)
        {
            _logger.LogDebug("PredictionService: only {Count} data points — skipping prediction", points.Count);
            _predictions.OnNext(Array.Empty<ResourcePrediction>());
            return;
        }

        // Derive samples-per-hour from actual data timestamps for test accuracy and real-world correctness.
        // Use the span from the first to last point; fall back to 30s default if span is zero.
        double samplesPerHour = DefaultSamplesPerHour;
        if (points.Count >= 2)
        {
            var spanHours = (points[points.Count - 1].Timestamp - points[0].Timestamp).TotalHours;
            if (spanHours > 0)
                samplesPerHour = (points.Count - 1) / spanHours;
        }

        var results = new List<ResourcePrediction>();

        // ── Disk health trend ──────────────────────────────────────────────
        var diskScores = points.Select(p => p.Disk).ToArray();
        if (TryBuildPrediction("Disk", diskScores, now, samplesPerHour, out var diskPrediction))
            results.Add(diskPrediction!);

        // ── Memory health trend ────────────────────────────────────────────
        var memScores = points.Select(p => p.Memory).ToArray();
        if (TryBuildPrediction("Memory", memScores, now, samplesPerHour, out var memPrediction))
            results.Add(memPrediction!);

        // Quiet Hours: suppress alert-tier logic is N/A this phase (no AlertsService integration).
        // Predictions are still emitted to the observable regardless of quiet hours state.
        if (_quietHours?.IsActive == true)
        {
            _logger.LogDebug("PredictionService: Quiet Hours active — predictions computed but alerts suppressed");
        }

        _predictions.OnNext(results);
    }

    /// <summary>
    /// Applies linear regression to <paramref name="scores"/> and, if the trend is
    /// meaningfully negative with a good R² fit, builds a <see cref="ResourcePrediction"/>.
    /// Returns <c>false</c> (and sets <paramref name="prediction"/> to null) when no
    /// actionable prediction can be made.
    /// </summary>
    private static bool TryBuildPrediction(
        string            resourceName,
        double[]          scores,
        DateTimeOffset    now,
        double            samplesPerHour,
        out ResourcePrediction? prediction)
    {
        prediction = null;

        var (slope, rSquared) = LinearRegression.Fit(scores);

        // Only flag a declining trend with a statistically decent fit
        if (slope >= 0 || rSquared <= RSquaredThreshold)
            return false;

        double currentScore  = scores[scores.Length - 1];
        // How many hours until the health score reaches 0 at this rate?
        double hoursToZero   = currentScore / (-slope * samplesPerHour);

        if (hoursToZero <= 0)
            return false;

        var severity         = hoursToZero < 24   ? RecommendationSeverity.Critical
                             : hoursToZero < 168  ? RecommendationSeverity.Warning
                             :                      RecommendationSeverity.Info;

        // Only emit Warning and Critical (Info is not actionable enough)
        if (severity == RecommendationSeverity.Info)
            return false;

        var depletionEstimate = now.Add(TimeSpan.FromHours(hoursToZero));
        var daysToZero        = hoursToZero / 24.0;

        string description = severity == RecommendationSeverity.Critical
            ? $"{resourceName} health declining — estimated critical in ~{(int)Math.Ceiling(hoursToZero)} hours"
            : $"{resourceName} health declining — estimated critical in ~{daysToZero:F0} days";

        prediction = new ResourcePrediction(
            Resource:          resourceName,
            Description:       description,
            DepletionEstimate: depletionEstimate,
            Confidence:        rSquared,
            Severity:          severity);

        return true;
    }
}
