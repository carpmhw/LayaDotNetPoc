using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Performance;

public sealed class LayaBenchmarkStatisticsTests
{
    /// <summary>驗證 benchmark percentile 使用排序樣本的線性插值。</summary>
    [Fact]
    public void Percentile_UsesLinearInterpolation()
    {
        var values = new[] { 1d, 2d, 3d, 4d };

        Assert.Equal(2.5, LayaBenchmarkStatistics.Percentile(values, 0.50), precision: 10);
        Assert.Equal(3.85, LayaBenchmarkStatistics.Percentile(values, 0.95), precision: 10);
        Assert.Equal(3.97, LayaBenchmarkStatistics.Percentile(values, 0.99), precision: 10);
    }

    /// <summary>驗證統計只接收 measured samples 並保存 warmup 與樣本數。</summary>
    [Fact]
    public void Summarize_PreservesWarmupAndMeasuredCounts()
    {
        var summary = LayaBenchmarkStatistics.Summarize(new[] { 1d, 2d, 3d, 4d }, 5);

        Assert.Equal(5, summary.WarmupCount);
        Assert.Equal(4, summary.SampleCount);
        Assert.Equal(2.5, summary.MeanMs, precision: 10);
        Assert.Equal(1d, summary.MinMs, precision: 10);
        Assert.Equal(4d, summary.MaxMs, precision: 10);
    }

    /// <summary>驗證空 measured samples 不會產生虛構 latency 指標。</summary>
    [Fact]
    public void Summarize_RejectsEmptyMeasuredSamples()
    {
        Assert.Throws<ArgumentException>(
            () => LayaBenchmarkStatistics.Summarize(Array.Empty<double>(), 5));
    }
}
