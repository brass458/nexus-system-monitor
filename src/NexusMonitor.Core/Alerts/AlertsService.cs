using System.Reactive.Linq;
using System.Reactive.Subjects;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;

namespace NexusMonitor.Core.Alerts;

/// <summary>
/// Background service that monitors system metrics and fires <see cref="AlertEvent"/>s
/// when configured thresholds are exceeded for a sustained period.
/// </summary>
public sealed class AlertsService : IDisposable
{
    private readonly ISystemMetricsProvider  _metrics;
    private readonly AppSettings             _settings;
    private readonly INotificationService    _notifications;

    // Sustain tracking: first moment this rule's value crossed the threshold
    private readonly Dictionary<Guid, DateTime> _firstSeen  = new();
    // Cooldown tracking: last time an alert was fired for a rule
    private readonly Dictionary<Guid, DateTime> _lastFired  = new();

    private readonly Subject<AlertEvent> _events = new();
    private IDisposable? _subscription;
    private bool _running;
    private int  _alertCount;

    public IObservable<AlertEvent> Events     => _events.AsObservable();
    public bool                    IsRunning  => _running;
    public int                     AlertCount => _alertCount;

    public AlertsService(ISystemMetricsProvider metrics, AppSettings settings,
                         INotificationService notifications)
    {
        _metrics       = metrics;
        _settings      = settings;
        _notifications = notifications;
    }

    /// <summary>Start the monitoring loop. Safe to call multiple times.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _subscription = _metrics
            .GetMetricsStream(TimeSpan.FromSeconds(2))
            .Subscribe(OnTick, ex => { _running = false; });
    }

    /// <summary>Stop the monitoring loop.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _subscription?.Dispose();
        _subscription = null;
    }

    private void OnTick(SystemMetrics m)
    {
        var rules = _settings.AlertRules ?? [];
        if (rules.Count == 0) return;

        // Extract current values for each metric
        double cpuPercent = m.Cpu.TotalPercent;
        double ramPercent = m.Memory.UsedPercent;
        double diskPercent = m.Disks.Count > 0
            ? m.Disks.Max(d => d.ActivePercent)
            : 0.0;
        double gpuPercent = m.Gpus.Count > 0
            ? m.Gpus[0].UsagePercent
            : 0.0;
        double cpuTemp = m.Cpu.TemperatureCelsius;

        var now = DateTime.UtcNow;

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled) continue;

            double value = rule.Metric switch
            {
                AlertMetric.CpuPercent     => cpuPercent,
                AlertMetric.RamPercent     => ramPercent,
                AlertMetric.DiskPercent    => diskPercent,
                AlertMetric.GpuPercent     => gpuPercent,
                AlertMetric.CpuTemperature => cpuTemp,
                _                          => 0.0
            };

            if (value > rule.Threshold)
            {
                // Start sustain tracking if not yet tracking
                if (!_firstSeen.TryGetValue(rule.Id, out var firstSeen))
                {
                    firstSeen = now;
                    _firstSeen[rule.Id] = firstSeen;
                }

                double sustainedSeconds = (now - firstSeen).TotalSeconds;
                bool sustainMet = sustainedSeconds >= rule.SustainSec;

                // Check cooldown
                bool cooldownElapsed = !_lastFired.TryGetValue(rule.Id, out var lastFired)
                    || (now - lastFired).TotalSeconds >= rule.CooldownSec;

                if (sustainMet && cooldownElapsed)
                {
                    var alertEvent = new AlertEvent(rule, value, now);
                    _events.OnNext(alertEvent);
                    _lastFired[rule.Id] = now;
                    System.Threading.Interlocked.Increment(ref _alertCount);

                    // Fire desktop toast notification if enabled
                    if (_settings.DesktopNotificationsEnabled)
                        _notifications.ShowAlert(rule.Name, alertEvent.ValueDisplay, rule.Severity);
                }
            }
            else
            {
                // Value dropped below threshold — reset sustain tracking
                _firstSeen.Remove(rule.Id);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _events.Dispose();
    }
}
