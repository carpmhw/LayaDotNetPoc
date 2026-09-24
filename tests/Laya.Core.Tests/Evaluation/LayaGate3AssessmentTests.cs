using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class LayaGate3AssessmentTests
{
    /// <summary>驗證完整證據與 owner criteria 才能得到 PASS／YES。</summary>
    [Fact]
    public void Decide_ReturnsPassOnlyForCompleteReliableEvidence()
    {
        var decision = LayaGate3Assessment.Decide(CreateEvidence(
            evidenceComplete: true,
            ownerCriteriaAccepted: true,
            suggestionEvidence: true,
            autoReliable: true,
            memoryRisk: true));

        Assert.Equal(LayaGate3Status.Pass, decision.Status);
        Assert.Equal(LayaGate3Integration.Yes, decision.Integration);
        Assert.True(decision.AutoEnabled);
    }

    /// <summary>驗證 suggestion 有價值但 AUTO 不可靠時只能得到 PARTIAL。</summary>
    [Fact]
    public void Decide_ReturnsPartialForSuggestionOnlyEvidence()
    {
        var decision = LayaGate3Assessment.Decide(CreateEvidence(
            evidenceComplete: true,
            ownerCriteriaAccepted: false,
            suggestionEvidence: true,
            autoReliable: false,
            highConfidenceError: true));

        Assert.Equal(LayaGate3Status.Partial, decision.Status);
        Assert.Equal(LayaGate3Integration.SuggestionOnly, decision.Integration);
        Assert.False(decision.AutoEnabled);
    }

    /// <summary>驗證缺 parity／必要證據或 suggestion 時一律 FAIL／NO。</summary>
    [Fact]
    public void Decide_ReturnsFailWhenEvidenceCannotSupportIntegration()
    {
        var missingEvidence = LayaGate3Assessment.Decide(CreateEvidence(
            evidenceComplete: false,
            ownerCriteriaAccepted: true,
            suggestionEvidence: true,
            autoReliable: true));
        var noSuggestion = LayaGate3Assessment.Decide(CreateEvidence(
            evidenceComplete: true,
            ownerCriteriaAccepted: false,
            suggestionEvidence: false,
            autoReliable: false));

        Assert.Equal(LayaGate3Status.Fail, missingEvidence.Status);
        Assert.Equal(LayaGate3Integration.No, missingEvidence.Integration);
        Assert.Equal(LayaGate3Status.Fail, noSuggestion.Status);
        Assert.Equal(LayaGate3Integration.No, noSuggestion.Integration);
    }

    /// <summary>建立 Gate 3 測試用的完整五面向證據。</summary>
    private static LayaGate3Evidence CreateEvidence(
        bool evidenceComplete,
        bool ownerCriteriaAccepted,
        bool suggestionEvidence,
        bool autoReliable,
        bool highConfidenceError = false,
        bool memoryRisk = false)
    {
        return new LayaGate3Evidence(
            evidenceComplete,
            ParityComplete: true,
            QualityComplete: true,
            CalibrationComplete: true,
            PerformanceComplete: true,
            DeploymentComplete: true,
            ownerCriteriaAccepted,
            suggestionEvidence,
            autoReliable,
            highConfidenceError,
            memoryRisk);
    }
}
