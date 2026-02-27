using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Core.Automation;

public enum ProBalanceEventType { Throttled, Restored, Started, Stopped }

public record ProBalanceEvent(
    ProBalanceEventType Type,
    int Pid,
    string ProcessName,
    ProcessPriority OriginalPriority,
    ProcessPriority NewPriority,
    DateTime Timestamp)
{
    public string Description => Type switch
    {
        ProBalanceEventType.Throttled => $"Lowered '{ProcessName}' ({Pid}): {OriginalPriority} \u2192 {NewPriority}",
        ProBalanceEventType.Restored  => $"Restored '{ProcessName}' ({Pid}): {NewPriority}",
        ProBalanceEventType.Started   => "ProBalance activated",
        ProBalanceEventType.Stopped   => "ProBalance deactivated",
        _ => string.Empty
    };

    public string TimeDisplay => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string TypeIcon => Type switch
    {
        ProBalanceEventType.Throttled => "\u2b07",
        ProBalanceEventType.Restored  => "\u2b06",
        ProBalanceEventType.Started   => "\u25b6",
        ProBalanceEventType.Stopped   => "\u23f9",
        _ => "\u2022"
    };

    public string TypeColor => Type switch
    {
        ProBalanceEventType.Throttled => "#FF9F0A",
        ProBalanceEventType.Restored  => "#30D158",
        ProBalanceEventType.Started   => "#0A84FF",
        ProBalanceEventType.Stopped   => "#636366",
        _ => "#EBEBF5"
    };
}
