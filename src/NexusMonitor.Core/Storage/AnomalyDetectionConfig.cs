namespace NexusMonitor.Core.Storage;

/// <summary>
/// Configuration for the anomaly detection engine.
/// Built from <see cref="NexusMonitor.Core.Models.AppSettings"/> at application start.
/// </summary>
public sealed class AnomalyDetectionConfig
{
    public bool Enabled    { get; set; } = false;

    /// <summary>Sliding-window size in ticks (~2 min at 2 s interval = 60 samples).</summary>
    public int  WindowSize { get; set; } = 60;

    // ── Per-metric sigma thresholds ─────────────────────────────────────────
    public double SigmaCpu     { get; set; } = 2.5;
    public double SigmaMem     { get; set; } = 2.5;
    public double SigmaGpu     { get; set; } = 2.5;
    public double SigmaNet     { get; set; } = 3.0;
    public double SigmaProcess { get; set; } = 3.0;

    // ── Hard-ceiling thresholds (always Critical, independent of sigma) ─────
    public double CpuCeiling { get; set; } = 95.0;
    public double MemCeiling { get; set; } = 95.0;
    public double GpuCeiling { get; set; } = 98.0;

    /// <summary>Minimum seconds between repeated events per {eventType}:{metricName}.</summary>
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>Ignore new connections seen within this many seconds of startup.</summary>
    public int NewConnectionGracePeriodSeconds { get; set; } = 120;

    /// <summary>Maximum distinct remote endpoints tracked in the seen-endpoints set.</summary>
    public int NewConnectionMaxTracked { get; set; } = 2_000;

    /// <summary>Minimum per-process CPU delta above its sliding baseline to flag as a spike.</summary>
    public double ProcessSpikeMinDeltaCpu { get; set; } = 20.0;

    // ── Sensitivity presets ─────────────────────────────────────────────────

    /// <summary>
    /// Apply a named sensitivity preset: "Low" (σ×3.5), "Medium" (σ×2.5), "High" (σ×1.5).
    /// Leaves other config properties unchanged.
    /// </summary>
    public void ApplySensitivity(string sensitivity)
    {
        double s = sensitivity switch
        {
            "Low"  => 3.5,
            "High" => 1.5,
            _      => 2.5,   // Medium (default)
        };
        SigmaCpu     = s;
        SigmaMem     = s;
        SigmaGpu     = s;
        SigmaNet     = s + 0.5;    // network is noisier; keep slightly higher
        SigmaProcess = s + 0.5;
    }
}
