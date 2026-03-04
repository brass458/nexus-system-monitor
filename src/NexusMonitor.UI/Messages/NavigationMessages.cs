namespace NexusMonitor.UI.Messages;

public record NavigateToProcessMessage(int Pid);

/// <summary>Broadcast when the user changes the metrics polling interval in Settings.</summary>
public record MetricsIntervalChangedMessage(TimeSpan Interval);

/// <summary>Broadcast when the user toggles metrics recording on/off in Settings.</summary>
public record MetricsEnabledChangedMessage(bool Enabled);
