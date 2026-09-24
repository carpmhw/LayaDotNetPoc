namespace Laya.Core.Evaluation;

/// <summary>保存一組 measured latency 的統計結果與量測口徑。</summary>
public sealed record LayaBenchmarkDistributionSummary(
    int WarmupCount,
    int SampleCount,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MinMs,
    double MaxMs);

/// <summary>提供 benchmark 共用的可重算 percentile 與分布統計。</summary>
public static class LayaBenchmarkStatistics
{
    /// <summary>以排序樣本的線性插值計算指定分位數。</summary>
    public static double Percentile(IReadOnlyList<double> values, double probability)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one measured sample is required.", nameof(values));
        }

        if (probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var position = (sorted.Length - 1) * probability;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    /// <summary>以 measured samples 建立平均值、分位數與極值摘要。</summary>
    public static LayaBenchmarkDistributionSummary Summarize(
        IReadOnlyList<double> values,
        int warmupCount)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one measured sample is required.", nameof(values));
        }

        if (warmupCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warmupCount));
        }

        var sorted = values.OrderBy(value => value).ToArray();
        return new LayaBenchmarkDistributionSummary(
            warmupCount,
            sorted.Length,
            sorted.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[0],
            sorted[^1]);
    }
}
