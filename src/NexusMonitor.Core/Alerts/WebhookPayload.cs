namespace NexusMonitor.Core.Alerts;

public sealed record WebhookPayload(
    string Alert,
    string Severity,
    string Timestamp,
    string Hostname,
    Dictionary<string, object>? Metrics);
