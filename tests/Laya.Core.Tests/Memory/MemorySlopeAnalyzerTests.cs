extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Analysis;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemorySlopeAnalyzerTests
{
    /// <summary>驗證 OLS slope、bytes／100 requests、endpoint delta 與 R-squared。</summary>
    [Fact]
    public void AnalyzeSegment_CalculatesLinearGrowthStatistics()
    {
        var samples = new[]
        {
            new MemorySamplePoint(0, 100, 0),
            new MemorySamplePoint(100, 200, 1),
            new MemorySamplePoint(300, 400, 2),
            new MemorySamplePoint(500, 600, 3)
        };

        var result = MemorySlopeAnalyzer.AnalyzeSegment(samples, startRequest: 0, endRequest: 500);

        Assert.True(result.IsComplete);
        Assert.Equal(100, result.SlopeBytesPer100Requests!.Value, precision: 6);
        Assert.Equal(500, result.EndpointDeltaBytes);
        Assert.Equal(1, result.RSquared!.Value, precision: 6);
        Assert.Equal(4, result.SampleCount);
    }

    /// <summary>驗證 forced-GC samples 不會污染自然 growth segment。</summary>
    [Fact]
    public void AnalyzeSegment_ExcludesForcedGcSamples()
    {
        var samples = new[]
        {
            new MemorySamplePoint(0, 100, 0),
            new MemorySamplePoint(100, 200, 1),
            new MemorySamplePoint(100, 900_000, 2, isForcedGc: true),
            new MemorySamplePoint(500, 600, 3)
        };

        var result = MemorySlopeAnalyzer.AnalyzeSegment(samples, 0, 500);

        Assert.Equal(100, result.SlopeBytesPer100Requests!.Value, precision: 6);
        Assert.Equal(3, result.SampleCount);
    }

    /// <summary>驗證缺少 exact endpoint 時回報 incomplete 而不以零 slope 代替。</summary>
    [Fact]
    public void AnalyzeSegment_RequiresBothExactEndpoints()
    {
        var samples = new[]
        {
            new MemorySamplePoint(10, 100, 0),
            new MemorySamplePoint(250, 200, 1)
        };

        var result = MemorySlopeAnalyzer.AnalyzeSegment(samples, 0, 500);

        Assert.False(result.IsComplete);
        Assert.Null(result.SlopeBytesPer100Requests);
        Assert.Contains("endpoint", result.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證任一 checkpoint 的 metric unavailable 時該 segment 不會以零值擬合。</summary>
    [Fact]
    public void AnalyzeSegment_BlocksUnavailableMetricWithinInterval()
    {
        var samples = new[]
        {
            new MemorySamplePoint(0, 100, 0),
            new MemorySamplePoint(250, null, 1),
            new MemorySamplePoint(500, 600, 2)
        };

        var result = MemorySlopeAnalyzer.AnalyzeSegment(samples, 0, 500);

        Assert.False(result.IsComplete);
        Assert.Null(result.SlopeBytesPer100Requests);
        Assert.Null(result.EndpointDeltaBytes);
        Assert.Contains("unavailable", result.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證 constant-memory plateau 的 slope 為零且水平線 R-squared 為一。</summary>
    [Fact]
    public void AnalyzeSegment_HandlesFlatPlateauWithoutNan()
    {
        var samples = new[]
        {
            new MemorySamplePoint(0, 2048, 0),
            new MemorySamplePoint(100, 2048, 1),
            new MemorySamplePoint(500, 2048, 2)
        };

        var result = MemorySlopeAnalyzer.AnalyzeSegment(samples, 0, 500);

        Assert.True(result.IsComplete);
        Assert.Equal(0, result.SlopeBytesPer100Requests!.Value);
        Assert.Equal(1, result.RSquared!.Value);
    }
}
