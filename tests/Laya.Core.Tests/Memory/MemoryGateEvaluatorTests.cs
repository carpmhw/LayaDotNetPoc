extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Analysis;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryGateEvaluatorTests
{
    /// <summary>驗證必要 evidence 不完整時 gate 與 classification 保持 null。</summary>
    [Fact]
    public void Evaluate_BlocksMissingEvidenceWithoutInventingClassification()
    {
        var decision = MemoryGateEvaluator.Evaluate(new MemoryGateEvidence());

        Assert.Equal(MemoryEvidenceStatus.Blocked, decision.EvidenceStatus);
        Assert.Null(decision.Status);
        Assert.Null(decision.Classification);
        Assert.Equal("DO NOT PROCEED", decision.Recommendation);
    }

    /// <summary>驗證 bounded PrivateMemory／RSS plateau 可通過，即使 WorkingSet 仍偏高。</summary>
    [Fact]
    public void Evaluate_PassesCompleteStableSoakWithHighWorkingSetOnly()
    {
        var evidence = CreatePassEvidence() with { WorkingSetPeakAboveDiagnosticBoundary = true };

        var decision = MemoryGateEvaluator.Evaluate(evidence);

        Assert.Equal(MemoryGateStatus.Pass, decision.Status);
        Assert.Equal(MemoryDiagnosticClassification.NoLeakEvidence, decision.Classification);
        Assert.Equal("PROCEED", decision.Recommendation);
    }

    /// <summary>驗證有界且解釋充分的 native retention 只允許有條件通過。</summary>
    [Fact]
    public void Evaluate_ReturnsPartialForBoundedNativeRetention()
    {
        var evidence = CreatePassEvidence() with
        {
            PrivateMemoryPlateau = false,
            RssPlateau = false,
            NativeRetentionObserved = true,
            BoundedNativeRetention = true,
            NativeRetentionExplained = true,
            DeploymentLimitDocumented = true,
            MonitoringConditionsDocumented = true
        };

        var decision = MemoryGateEvaluator.Evaluate(evidence);

        Assert.Equal(MemoryGateStatus.Partial, decision.Status);
        Assert.Equal(MemoryDiagnosticClassification.NativeRetentionSuspected, decision.Classification);
        Assert.Equal("PROCEED WITH CONDITIONS", decision.Recommendation);
    }

    /// <summary>驗證 repeated target OOM 與 Leak Confirmed 優先判定 FAIL。</summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public void Evaluate_FailsPersistentMemoryGateViolations(
        bool targetRepeatedOom,
        bool reproducedRetentionSource,
        bool lifecycleAuditConfirmsUndisposed)
    {
        var evidence = CreatePassEvidence() with
        {
            TargetDockerRepeatedOom = targetRepeatedOom,
            LeakReproduced = reproducedRetentionSource,
            RetentionSourceIdentified = reproducedRetentionSource,
            LifecycleAuditConfirmsUndisposedResource = lifecycleAuditConfirmsUndisposed
        };

        var decision = MemoryGateEvaluator.Evaluate(evidence);

        Assert.Equal(MemoryGateStatus.Fail, decision.Status);
        Assert.Equal("DO NOT PROCEED", decision.Recommendation);
        if (reproducedRetentionSource)
        {
            Assert.Equal(MemoryDiagnosticClassification.LeakConfirmed, decision.Classification);
        }
    }

    /// <summary>建立符合 PASS 條件的完整 synthetic gate input。</summary>
    private static MemoryGateEvidence CreatePassEvidence()
    {
        return new MemoryGateEvidence
        {
            EvidenceComplete = true,
            FullPipelineRequests = 5000,
            CrashCount = 0,
            OomCount = 0,
            Errors = 0,
            IntegrityFailures = 0,
            PrivateMemoryPlateau = true,
            RssPlateau = true,
            ManagedHeapStable = true,
            TargetDockerStable = true,
            ReplicatesConsistent = true
        };
    }
}
