using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Services;

namespace NexusMonitor.Platform.MacOS;

/// <summary>
/// macOS notification service using osascript display notification.
/// Works on all macOS versions without entitlement requirements.
/// </summary>
public sealed class MacOSNotificationService : INotificationService
{
    public bool IsSupported => true;

    public void ShowAlert(string ruleName, string metricDisplay, AlertSeverity severity)
    {
        string title = $"Nexus Monitor — {ruleName}";
        SendNotification(title, metricDisplay);
    }

    public void ShowAnomaly(string eventType, string description, int severity)
    {
        string title = $"Anomaly — {eventType}";
        SendNotification(title, description);
    }

    private static void SendNotification(string title, string body)
    {
        try
        {
            // Escape single quotes for AppleScript string literals
            string safeTitle = title.Replace("'", "\'");
            string safeBody  = body.Replace("'", "\'");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = "osascript",
                Arguments              = $"-e 'display notification \"{safeBody}\" with title \"{safeTitle}\"'",
                UseShellExecute        = false,
                RedirectStandardOutput = false,
                RedirectStandardError  = false,
                CreateNoWindow         = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Swallow — notifications must never crash the app
        }
    }
}
