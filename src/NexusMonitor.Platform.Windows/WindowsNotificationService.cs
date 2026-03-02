using System.Runtime.InteropServices;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Services;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Windows toast notification service using WinRT Windows.UI.Notifications.
/// Requires net8.0-windows10.0.17763.0 TFM.
/// </summary>
public sealed partial class WindowsNotificationService : INotificationService
{
    private const string AppId = "NexusMonitor.SystemMonitor";

    private readonly ToastNotifier? _notifier;

    public bool IsSupported => _notifier is not null;

    public WindowsNotificationService()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
        }
        catch
        {
            // Unsupported environment — IsSupported stays false
        }
    }

    public void ShowAlert(string ruleName, string metricDisplay, AlertSeverity severity)
    {
        if (_notifier is null) return;

        string icon = severity switch
        {
            AlertSeverity.Critical => "🔴",
            AlertSeverity.Warning  => "⚠️",
            _                      => "ℹ️"
        };

        ShowToast($"{icon} Nexus Monitor — {EscapeXml(ruleName)}", EscapeXml(metricDisplay));
    }

    public void ShowAnomaly(string eventType, string description, int severity)
    {
        if (_notifier is null) return;

        string icon = severity switch
        {
            2 => "🔴",  // Critical
            1 => "⚠️",  // Warning
            _ => "ℹ️"   // Info
        };

        ShowToast($"{icon} Anomaly — {EscapeXml(eventType)}", EscapeXml(description));
    }

    private void ShowToast(string title, string body)
    {
        if (_notifier is null) return;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast duration="short">
                  <visual><binding template="ToastGeneric">
                    <text>{title}</text>
                    <text>{body}</text>
                  </binding></visual>
                </toast>
                """);
            _notifier.Show(new ToastNotification(xml));
        }
        catch
        {
            // Swallow — notifications must never crash the app
        }
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;");

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string AppID);
}
