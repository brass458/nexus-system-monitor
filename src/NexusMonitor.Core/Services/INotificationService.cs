using NexusMonitor.Core.Alerts;

namespace NexusMonitor.Core.Services;

/// <summary>
/// Abstraction for showing system-level desktop notifications.
/// Implementations may use Windows Toast, macOS NSUserNotification, etc.
/// </summary>
public interface INotificationService
{
    /// <summary>True when the platform supports desktop notifications.</summary>
    bool IsSupported { get; }

    /// <summary>Show an alert notification for a triggered rule.</summary>
    void ShowAlert(string ruleName, string metricDisplay, AlertSeverity severity);

    /// <summary>Show an anomaly detection notification.</summary>
    void ShowAnomaly(string eventType, string description, int severity);
}

/// <summary>
/// No-op fallback used on platforms without native notification support.
/// </summary>
public sealed class NullNotificationService : INotificationService
{
    public bool IsSupported => false;
    public void ShowAlert(string ruleName, string metricDisplay, AlertSeverity severity) { }
    public void ShowAnomaly(string eventType, string description, int severity) { }
}
