namespace NexusMonitor.Core.Health;

public enum PredictionSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record ResourcePrediction(
    string Resource,
    string Description,
    DateTime? DepletionEstimate,
    double Confidence,
    PredictionSeverity Severity);
