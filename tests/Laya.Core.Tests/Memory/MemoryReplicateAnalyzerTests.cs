extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Analysis;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryReplicateAnalyzerTests
{
    /// <summary>驗證兩個一致 replicates 符合 policy 時不需要第三次量測。</summary>
    [Fact]
    public void Assess_TwoConsistentRunsPassWithinFrozenTolerance()
    {
        var assessment = MemoryReplicateAnalyzer.Assess(
            new[] { CreateMetric("run-1", 1000), CreateMetric("run-2", 1040) },
            CreateTolerance(0.10));

        Assert.True(assessment.IsConsistent);
        Assert.False(assessment.RequiresThirdRun);
        Assert.Null(assessment.Reason);
    }

    /// <summary>驗證兩個超出 frozen tolerance 的 replicates 需要第三次重現。</summary>
    [Fact]
    public void Assess_DivergentPairRequiresThirdRun()
    {
        var assessment = MemoryReplicateAnalyzer.Assess(
            new[] { CreateMetric("run-1", 1000), CreateMetric("run-2", 1400) },
            CreateTolerance(0.10));

        Assert.False(assessment.IsConsistent);
        Assert.True(assessment.RequiresThirdRun);
        Assert.False(string.IsNullOrWhiteSpace(assessment.Reason));
    }

    /// <summary>驗證第三次仍超界時保留 inconsistent conclusion 且不排程第四次。</summary>
    [Fact]
    public void Assess_ThreeDivergentRunsRemainInconsistentWithoutFourthRun()
    {
        var assessment = MemoryReplicateAnalyzer.Assess(
            new[] { CreateMetric("run-1", 1000), CreateMetric("run-2", 1400), CreateMetric("run-3", 1600) },
            CreateTolerance(0.10));

        Assert.False(assessment.IsConsistent);
        Assert.False(assessment.RequiresThirdRun);
        Assert.Contains("third", assessment.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證重複 run ID 不可被計成獨立 replicates。</summary>
    [Fact]
    public void Assess_RejectsDuplicateRunIds()
    {
        Assert.Throws<ArgumentException>(() => MemoryReplicateAnalyzer.Assess(
            new[] { CreateMetric("run-1", 1000), CreateMetric("run-1", 1000) },
            CreateTolerance(0.10)));
    }

    /// <summary>驗證正負方向 slope disagreement 不會因絕對值相同而被忽略。</summary>
    [Fact]
    public void Assess_DetectsOppositeSlopeDirections()
    {
        var first = CreateMetric("run-positive", 1000);
        var second = new MemoryReplicateMeasurement("run-negative", 1000, 1000, -100_000, 1000, 1000, -100_000);

        var assessment = MemoryReplicateAnalyzer.Assess(new[] { first, second }, CreateTolerance(0.10));

        Assert.False(assessment.IsConsistent);
        Assert.True(assessment.RequiresThirdRun);
    }

    /// <summary>建立一致六項 private/RSS peak/end/slope metrics。</summary>
    private static MemoryReplicateMeasurement CreateMetric(string runId, long value)
    {
        return new MemoryReplicateMeasurement(
            runId,
            value,
            value,
            100_000,
            value,
            value,
            100_000);
    }

    /// <summary>建立三項相同相對差異上限的 frozen replicate tolerance。</summary>
    private static MemoryReplicateTolerance CreateTolerance(double maximum)
    {
        return new MemoryReplicateTolerance(maximum, maximum, maximum);
    }
}
