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
    public string Name      { get; init; } = string.Empty;
    public string Command   { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string Location  { get; init; } = string.Empty;
    public bool   IsEnabled { get; init; } = true;
    public StartupItemType ItemType { get; init; }
}
