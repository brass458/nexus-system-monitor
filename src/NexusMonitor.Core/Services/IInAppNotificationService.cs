namespace NexusMonitor.Core.Services;

/// <summary>Severity levels for in-app notifications.</summary>
public enum InAppSeverity
{
    Info     = 0,
    Warning  = 1,
    Critical = 2,
}

/// <summary>Data for a single in-app toast notification.</summary>
public record InAppNotification(
    string        Title,
    string        Body,
    InAppSeverity Severity,
    TimeSpan      AutoDismiss);

/// <summary>
/// In-app notification channel: allows services to push iOS 26-style pill toasts
/// into the main window overlay without taking a dependency on UI types.
/// </summary>
public interface IInAppNotificationService
{
    /// <summary>Push a notification to the in-app overlay.</summary>
    void Show(InAppNotification notification);

    /// <summary>When true, Show() silently drops notifications. Set by QuietHoursService.</summary>
    bool IsSuppressed { get; set; }

    /// <summary>Observable stream of notifications as they are pushed.</summary>
    IObservable<InAppNotification> Notifications { get; }
}
