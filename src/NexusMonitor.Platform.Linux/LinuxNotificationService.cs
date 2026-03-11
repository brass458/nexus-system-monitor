using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Services;

namespace NexusMonitor.Platform.Linux;

/// <summary>
/// Linux notification service using notify-send (libnotify).
/// Falls back to no-op if notify-send is not installed.
/// </summary>
public sealed class LinuxNotificationService : INotificationService
{
    private readonly bool _available;

    public bool IsSupported => _available;

    public LinuxNotificationService()
    {
        // Detect notify-send at construction time
        try
        {
            using var check = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName               = "which",
                Arguments              = "notify-send",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            });
            check?.WaitForExit(1000);
            _available = check?.ExitCode == 0;
        }
        catch
        {
            _available = false;
        }
    }

    public void ShowAlert(string ruleName, string metricDisplay, AlertSeverity severity)
    {
        if (!_available) return;
        string urgency = severity switch
        {
            AlertSeverity.Critical => "critical",
            AlertSeverity.Warning  => "normal",
            _                      => "low"
        };
        Send($"Nexus Monitor — {ruleName}", metricDisplay, urgency);
    }

    public void ShowAnomaly(string eventType, string description, int severity)
    {
        if (!_available) return;
        string urgency = severity switch
        {
            2 => "critical",
            1 => "normal",
            _ => "low"
        };
        Send($"Anomaly — {eventType}", description, urgency);
    }

    private static void Send(string title, string body, string urgency)
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName               = "notify-send",
                ArgumentList           = { "-u", urgency, "--", title, body },
                UseShellExecute        = false,
                RedirectStandardOutput = false,
                CreateNoWindow         = true,
            });
        }
        catch
        {
            // Swallow — notifications must never crash the app
        }
    }
}
