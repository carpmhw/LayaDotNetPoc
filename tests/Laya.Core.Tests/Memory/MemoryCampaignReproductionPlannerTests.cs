extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Analysis;
using MemoryBenchmarks::Laya.MemoryBenchmarks.Orchestration;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryCampaignReproductionPlannerTests
{
    /// <summary>驗證兩次 run 的 frozen tolerance disagreement 只排程該組第三次 replicate。</summary>
    [Fact]
    public void PlanThirdReplicates_AddsOnlyDivergentGroup()
    {
        var plan = MemoryCampaignPlanner.CreatePlan("reproduction", "campaign-test-001", "formal");
        var measurements = new Dictionary<string, MemoryReplicateMeasurement>(StringComparer.Ordinal);
        foreach (var run in plan)
        {
            var peak = run.StepId == "repro-arena-off-r2" ? 1_500L : 1_000L;
            measurements.Add(run.RunId, CreateMetrics(run.RunId, peak));
        }

        var thirdRuns = MemoryCampaignReproductionPlanner.PlanThirdReplicates(
            plan,
            measurements,
            CreateTolerance(0.10));

        var thirdRun = Assert.Single(thirdRuns);
        Assert.Equal("repro-arena-off-r3", thirdRun.StepId);
        Assert.Equal("campaign-test-001-repro-arena-off-r3", thirdRun.RunId);
    }

    /// <summary>驗證已一致的兩次 replicates 不會新增費用型第三次 run。</summary>
    [Fact]
    public void PlanThirdReplicates_DoesNotAddRunWhenPairIsConsistent()
    {
        var plan = MemoryCampaignPlanner.CreatePlan("reproduction", "campaign-test-002", "formal");
        var measurements = plan.ToDictionary(
            run => run.RunId,
            run => CreateMetrics(run.RunId, 1000),
            StringComparer.Ordinal);

        var thirdRuns = MemoryCampaignReproductionPlanner.PlanThirdReplicates(
            plan,
            measurements,
            CreateTolerance(0.10));

        Assert.Empty(thirdRuns);
    }

    /// <summary>建立 memory metrics，讓指定 PrivateMemory peak 可控制 replication delta。</summary>
    private static MemoryReplicateMeasurement CreateMetrics(string runId, long peak)
    {
        return new MemoryReplicateMeasurement(runId, peak, peak, 100, peak, peak, 100);
    }

    /// <summary>建立一組固定相對差異 tolerances。</summary>
    private static MemoryReplicateTolerance CreateTolerance(double tolerance)
    {
        return new MemoryReplicateTolerance(tolerance, tolerance, tolerance);
    }
}
