namespace NexusMonitor.Core.Gaming;

public interface IPowerPlanProvider
{
    IReadOnlyList<PowerPlanInfo> GetPowerPlans();
    Guid GetActivePlan();
    void SetActivePlan(Guid schemeGuid);

    // Well-known GUIDs
    static readonly Guid Balanced        = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    static readonly Guid PowerSaver      = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    // Ultimate Performance (Windows 10 1803+, may not exist on all machines)
    static readonly Guid UltimatePerf    = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
}
