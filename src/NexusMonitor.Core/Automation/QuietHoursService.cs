using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Microsoft.Extensions.Logging;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Automation;

/// <summary>
/// Evaluates whether the current time falls inside the user-configured Quiet Hours window
/// and fires <see cref="IsActiveChanged"/> whenever the active/inactive state changes.
/// Start() schedules a 1-minute timer that calls EvaluateCurrent(); Stop() cancels it.
/// </summary>
public sealed class QuietHoursService : IDisposable
{
    private readonly AppSettings      _settings;
    private readonly Func<DateTime>   _clock;
    private readonly ILogger<QuietHoursService>? _logger;
    private readonly Subject<bool>    _isActiveChanged = new();
    private Timer?                    _timer;
    private volatile bool             _isActive;

    public bool IsActive => _isActive;
    public IObservable<bool> IsActiveChanged => _isActiveChanged.AsObservable();

    /// <summary>Production constructor — uses real clock.</summary>
    public QuietHoursService(AppSettings settings, ILogger<QuietHoursService>? logger = null)
        : this(settings, () => DateTime.Now, logger) { }

    /// <summary>Test constructor — accepts clock injection.</summary>
    public QuietHoursService(AppSettings settings, Func<DateTime> clock,
        ILogger<QuietHoursService>? logger = null)
    {
        _settings = settings;
        _clock    = clock;
        _logger   = logger;
        _isActive = ComputeIsActive(_clock());
    }

    public void Start()
    {
        if (_timer != null) return;
        _timer = new Timer(_ => EvaluateCurrent(), null,
            TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void EvaluateCurrent()
    {
        try
        {
            var newActive = ComputeIsActive(_clock());
            if (newActive == _isActive) return;
            _isActive = newActive;
            _isActiveChanged.OnNext(newActive);
            _logger?.LogInformation("Quiet Hours {State}", newActive ? "activated" : "deactivated");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "QuietHoursService.EvaluateCurrent error");
        }
    }

    private bool ComputeIsActive(DateTime now)
    {
        if (!_settings.QuietHoursEnabled) return false;

        if (!TryParseTimeOfDay(_settings.QuietHoursStart, out var start)) return false;
        if (!TryParseTimeOfDay(_settings.QuietHoursEnd, out var end)) return false;

        var tod = now.TimeOfDay;

        if (_settings.QuietHoursDays.Count > 0)
        {
            bool isOvernightAfterMidnight = end <= start && tod < start;
            var effectiveDay = isOvernightAfterMidnight
                ? now.AddDays(-1).DayOfWeek
                : now.DayOfWeek;

            if (!_settings.QuietHoursDays.Contains(effectiveDay)) return false;
        }

        if (end <= start)
            return tod >= start || tod < end;

        return tod >= start && tod < end;
    }

    /// <summary>
    /// Parses a "H:mm" or "HH:mm" time string into a TimeSpan (time-of-day).
    /// Accepts single or double digit hours (0-23).
    /// </summary>
    private static bool TryParseTimeOfDay(string? value, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // TimeSpan.TryParse handles "H:mm" and "HH:mm" natively (e.g. "9:00", "22:00")
        // It interprets h:mm as hh:mm:ss when h is ambiguous, so we use explicit splitting.
        var parts = value.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int hours)) return false;
        if (!int.TryParse(parts[1], out int minutes)) return false;
        if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59) return false;

        result = new TimeSpan(hours, minutes, 0);
        return true;
    }

    public void Dispose()
    {
        Stop();
        _isActiveChanged.Dispose();
    }
}
