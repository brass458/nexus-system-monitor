namespace NexusMonitor.Core.Models;

public enum StartupItemType
{
    RegistryCurrentUser,
    RegistryLocalMachine,
    StartupFolder,
    TaskScheduler,
}

public record StartupItem
{
    public string Name          { get; init; } = string.Empty;
    public string Command       { get; init; } = string.Empty;
    public string Publisher     { get; init; } = string.Empty;
    public string Location      { get; init; } = string.Empty;
    public bool   IsEnabled     { get; init; } = true;
    public StartupItemType ItemType { get; init; }

    /// <summary>
    /// Estimated startup impact level.
    /// Derived from item type / location; detailed boot-time measurement requires ETW.
    /// </summary>
    public string StartupImpact { get; init; } = "Not measured";
}
