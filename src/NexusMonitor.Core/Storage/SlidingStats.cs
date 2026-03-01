namespace NexusMonitor.Core.Storage;

/// <summary>
/// Fixed-size circular buffer that computes mean and standard deviation over
/// the most recent <see cref="WindowSize"/> samples.
///
/// Thread-safety: not thread-safe; callers must synchronise if needed.
/// </summary>
public sealed class SlidingStats
{
    private readonly double[] _buf;
    private int _head;   // next write index
    private int _count;  // samples stored so far (capped at WindowSize)

    public int WindowSize { get; }

    public SlidingStats(int windowSize)
    {
        WindowSize = windowSize;
        _buf  = new double[windowSize];
    }

    /// <summary>Push a new sample. Evicts the oldest when the buffer is full.</summary>
    public void Push(double value)
    {
        _buf[_head] = value;
        _head = (_head + 1) % WindowSize;
        if (_count < WindowSize) _count++;
    }

    /// <summary>
    /// Returns true when <paramref name="value"/> exceeds mean + k×σ over the
    /// current window. Requires at least WindowSize/2 samples (warmup guard).
    /// </summary>
    public bool IsAnomaly(double value, double kSigma)
    {
        if (_count < WindowSize / 2) return false;

        double mean   = ComputeMean();
        double stdDev = ComputeStdDev(mean);

        return value > mean + kSigma * stdDev;
    }

    /// <summary>Returns the current mean of the window (0 if empty).</summary>
    public double Mean() => _count == 0 ? 0 : ComputeMean();

    // ── Internal helpers ───────────────────────────────────────────────────

    private double ComputeMean()
    {
        // When not full: valid values are at _buf[0.._count-1].
        // When full:     all WindowSize positions are valid.
        // Both cases are covered by iterating 0.._count-1.
        double sum = 0;
        for (int i = 0; i < _count; i++) sum += _buf[i];
        return sum / _count;
    }

    private double ComputeStdDev(double mean)
    {
        double variance = 0;
        for (int i = 0; i < _count; i++)
        {
            double d = _buf[i] - mean;
            variance += d * d;
        }
        return Math.Sqrt(variance / _count);
    }
}
