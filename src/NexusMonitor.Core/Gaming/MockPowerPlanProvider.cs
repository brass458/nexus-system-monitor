namespace NexusMonitor.Core.Gaming;

/// <summary>
/// In-memory mock for non-Windows builds and unit tests.
/// Reports three plans with Balanced active; SetActivePlan is a no-op.
/// </summary>
public sealed class MockPowerPlanProvider : IPowerPlanProvider
{
    private static readonly IReadOnlyList<PowerPlanInfo> _plans =
    [
        new(IPowerPlanProvider.PowerSaver,      "Power Saver",      false),
        new(IPowerPlanProvider.Balanced,         "Balanced",         true),
        new(IPowerPlanProvider.HighPerformance,  "High Performance", false),
    ];

    public IReadOnlyList<PowerPlanInfo> GetPowerPlans() => _plans;

    public Guid GetActivePlan() => IPowerPlanProvider.Balanced;

    public void SetActivePlan(Guid schemeGuid)
    {
        // No-op in mock — real implementation is in WindowsPowerPlanProvider
    }
}
