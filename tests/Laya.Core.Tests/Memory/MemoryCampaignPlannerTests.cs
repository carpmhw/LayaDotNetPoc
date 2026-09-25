extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Orchestration;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryCampaignPlannerTests
{
    /// <summary>驗證 baseline plan 固定 multilingual short、單題、arena ON 與 5000 requests。</summary>
    [Fact]
    public void CreatePlan_BaselineMatchesFrozenWorkloadShape()
    {
        var plan = MemoryCampaignPlanner.CreatePlan("baseline", "campaign-20260924", "pilot");

        var invocation = Assert.Single(plan);
        Assert.Equal("full-pipeline", invocation.Scenario);
        Assert.Equal(5000, invocation.Requests);
        Assert.Equal(1, invocation.Concurrency);
        Assert.True(invocation.CpuArenaEnabled);
        Assert.Equal("short", invocation.StateProfile);
        Assert.Equal(1, invocation.QuestionCount);
        Assert.Equal("multilingual", invocation.ProfileName);
    }

    /// <summary>驗證 component plan 涵蓋三語 tokenizer 與隔離 sequence／tensor／calibration／run-only。</summary>
    [Fact]
    public void CreatePlan_ComponentsIncludesRequiredScenariosAndCounts()
    {
        var plan = MemoryCampaignPlanner.CreatePlan("components", "campaign-20260924", "pilot");

        Assert.Equal(8, plan.Count);
        Assert.Equal(3, plan.Count(run => run.Scenario == "tokenizer-only" && run.Requests == 10000));
        Assert.Equal(1, plan.Count(run => run.Scenario == "sequence-only" && run.Requests == 10000));
        Assert.Equal(1, plan.Count(run => run.Scenario == "tensor-only" && run.Requests == 10000));
        Assert.Equal(1, plan.Count(run => run.Scenario == "calibration-only" && run.Requests == 10000));
        Assert.Equal(1, plan.Count(run => run.Scenario == "run-only" && run.Requests == 5000));
        Assert.Equal(8, plan.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, Assert.Single(plan, run => run.Scenario == "calibration-only").QuestionCount);
    }

    /// <summary>驗證 concurrency plan 僅使用 shared full-pipeline 與 1／2／4 workers。</summary>
    [Fact]
    public void CreatePlan_ConcurrencyUsesSharedPipelineAtAllLevels()
    {
        var plan = MemoryCampaignPlanner.CreatePlan("concurrency", "campaign-20260924", "formal");

        Assert.Equal(new[] { 1, 2, 4 }, plan.Select(run => run.Concurrency));
        Assert.All(plan, run => Assert.Equal("full-pipeline", run.Scenario));
        Assert.All(plan, run => Assert.Equal(1000, run.Requests));
        Assert.All(plan, run => Assert.Equal(2, run.QuestionCount));
    }

    /// <summary>驗證 short／one-question 設定在兩個 shape 維度共用唯一 run ID。</summary>
    [Fact]
    public void CreatePlan_ShapeMatrixReusesShortSingleQuestionInvocation()
    {
        var plan = MemoryCampaignPlanner.CreatePlan("shape", "campaign-20260924", "pilot");

        Assert.Equal(7, plan.Count);
        Assert.Equal(plan.Count, plan.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count());
        Assert.Single(plan, run => run.StepId == "shape-short-q1" && run.Requests == 1000);
        Assert.Single(plan, run => run.StepId == "shape-question-short-q1" && run.Requests == 1000);
        Assert.Contains(plan, run => run.StateProfile == "medium" && run.QuestionCount == 1);
        Assert.Contains(plan, run => run.StateProfile == "long" && run.QuestionCount == 1);
        Assert.Contains(plan, run => run.StateProfile == "short" && run.QuestionCount == 2);
        Assert.Contains(plan, run => run.StateProfile == "short" && run.QuestionCount == 5);
        var retention = Assert.Single(plan, run => run.StateSchedule is not null);
        Assert.Equal(3000, retention.Requests);
        Assert.Equal("short:1000,long:1000,short:1000", retention.StateSchedule);
    }

    /// <summary>驗證 reproducibility planner 可在兩次不同 run 後建立唯一第三次 replicate。</summary>
    [Fact]
    public void CreateThirdReplicate_UsesNewRunIdAndPreservesGroupShape()
    {
        var initial = MemoryCampaignPlanner.CreatePlan("reproduction", "campaign-20260924", "formal");
        var firstBaseline = Assert.Single(initial, run => run.StepId == "repro-baseline-r1");

        var third = MemoryCampaignPlanner.CreateThirdReplicate(firstBaseline, 3);

        Assert.Equal("repro-baseline-r3", third.StepId);
        Assert.Equal("campaign-20260924-repro-baseline-r3", third.RunId);
        Assert.DoesNotContain(initial, run => run.RunId == third.RunId);
        Assert.Equal(firstBaseline.Scenario, third.Scenario);
        Assert.Equal(firstBaseline.Requests, third.Requests);
        Assert.Equal(firstBaseline.Concurrency, third.Concurrency);
    }

    /// <summary>驗證未知 stage、空 campaign ID 與非法 mode 均明確拒絕。</summary>
    [Theory]
    [InlineData("unknown", "campaign-20260924", "pilot")]
    [InlineData("baseline", "", "pilot")]
    [InlineData("baseline", "campaign-20260924", "auto")]
    public void CreatePlan_RejectsInvalidPlanIdentity(string stage, string campaignId, string mode)
    {
        Assert.Throws<ArgumentException>(() => MemoryCampaignPlanner.CreatePlan(stage, campaignId, mode));
    }
}
