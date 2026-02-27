namespace NexusMonitor.UI.Messages;

public record NavigateToProcessMessage(int Pid);

/// <summary>Broadcast when the user changes the metrics polling interval in Settings.</summary>
public record MetricsIntervalChangedMessage(TimeSpan Interval);
