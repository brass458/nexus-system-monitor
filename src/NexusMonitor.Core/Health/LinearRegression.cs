namespace NexusMonitor.Core.Health;

internal static class LinearRegression
{
    /// <summary>
    /// Performs a least-squares linear fit on evenly-spaced samples (x = 0,1,2,…).
    /// Returns the slope per sample and R² goodness-of-fit.
    /// </summary>
    public static (double Slope, double RSquared) Fit(ReadOnlySpan<double> y)
    {
        int n = y.Length;
        if (n < 2) return (0, 0);

        double sumY = 0, sumXY = 0;
        for (int i = 0; i < n; i++)
        {
            sumY  += y[i];
            sumXY += i * y[i];
        }

        // Σx = n*(n-1)/2,  Σx² = n*(n-1)*(2n-1)/6
        double sumX  = n * (n - 1) / 2.0;
        double sumX2 = n * (n - 1) * (2 * n - 1) / 6.0;

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-10) return (0, 0);

        double slope     = (n * sumXY - sumX * sumY) / denom;
        double intercept = (sumY - slope * sumX) / n;

        double meanY  = sumY / n;
        double ssTot  = 0, ssRes = 0;
        for (int i = 0; i < n; i++)
        {
            double predicted = slope * i + intercept;
            ssRes += (y[i] - predicted) * (y[i] - predicted);
            ssTot += (y[i] - meanY)     * (y[i] - meanY);
        }

        double rSquared = ssTot < 1e-10 ? 0 : 1 - ssRes / ssTot;
        return (slope, rSquared);
    }
}
