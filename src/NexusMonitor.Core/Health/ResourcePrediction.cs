namespace NexusMonitor.Core.Health;

/// <summary>
/// Represents a forward-looking prediction about a monitored resource,
/// including an optional depletion estimate and model confidence.
/// </summary>
/// <param name="Resource">The name of the monitored resource (e.g. "Memory", "Disk C:").</param>
/// <param name="Description">Human-readable description of the predicted trend.</param>
/// <param name="DepletionEstimate">
/// When the resource is projected to reach a critical threshold.
/// <c>null</c> if no depletion is projected within the forecast window.
/// </param>
/// <param name="Confidence">
/// R² goodness-of-fit for the underlying regression model, in the range 0.0–1.0.
/// Values closer to 1.0 indicate a stronger fit and higher prediction reliability.
/// </param>
/// <param name="Severity">Severity level of the predicted condition.</param>
public sealed record ResourcePrediction(
    string Resource,
    string Description,
    DateTimeOffset? DepletionEstimate,
    double Confidence,
    RecommendationSeverity Severity);
