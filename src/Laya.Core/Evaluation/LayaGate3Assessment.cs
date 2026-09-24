namespace Laya.Core.Evaluation;

/// <summary>Gate 3 的三種固定決策狀態。</summary>
public enum LayaGate3Status
{
    /// <summary>必要證據完整且可建議下一階段 integration。</summary>
    Pass,

    /// <summary>只有 suggestion 證據可用，不啟用 AUTO。</summary>
    Partial,

    /// <summary>證據不足或品質／部署無法支持任何整合。</summary>
    Fail
}

/// <summary>Gate 3 對應的 integration 建議。</summary>
public enum LayaGate3Integration
{
    /// <summary>建議進入下一階段 integration，但不直接修改 production。</summary>
    Yes,

    /// <summary>僅允許 suggestion-only 研究用途。</summary>
    SuggestionOnly,

    /// <summary>不建議任何 integration。</summary>
    No
}

/// <summary>保存 Gate 3 決策所需的證據完整性與品質判斷。</summary>
public sealed record LayaGate3Evidence(
    bool EvidenceComplete,
    bool ParityComplete,
    bool QualityComplete,
    bool CalibrationComplete,
    bool PerformanceComplete,
    bool DeploymentComplete,
    bool OwnerCriteriaAccepted,
    bool SuggestionEvidence,
    bool AutoReliable,
    bool HighConfidenceError,
    bool MemoryRisk);

/// <summary>保存 Gate 3 狀態、integration 對應與可追溯理由。</summary>
public sealed record LayaGate3Decision(
    LayaGate3Status Status,
    LayaGate3Integration Integration,
    bool AutoEnabled,
    string Reason);

/// <summary>依固定證據契約計算 Gate 3，不把 POC target 當 production SLA。</summary>
public static class LayaGate3Assessment
{
    /// <summary>計算 PASS／PARTIAL／FAIL 與 YES／SUGGESTION-ONLY／NO 對應。</summary>
    public static LayaGate3Decision Decide(LayaGate3Evidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (!evidence.EvidenceComplete || !evidence.ParityComplete)
        {
            return new LayaGate3Decision(
                LayaGate3Status.Fail,
                LayaGate3Integration.No,
                false,
                "Required evidence or parity is incomplete; no model integration is supported.");
        }

        var qualityEvidenceComplete = evidence.QualityComplete &&
            evidence.CalibrationComplete &&
            evidence.PerformanceComplete &&
            evidence.DeploymentComplete;
        if (qualityEvidenceComplete && evidence.OwnerCriteriaAccepted &&
            evidence.AutoReliable && !evidence.HighConfidenceError)
        {
            return new LayaGate3Decision(
                LayaGate3Status.Pass,
                LayaGate3Integration.Yes,
                true,
                "Required evidence is complete and owner criteria support the measured automation candidate.");
        }

        if (qualityEvidenceComplete && evidence.SuggestionEvidence)
        {
            return new LayaGate3Decision(
                LayaGate3Status.Partial,
                LayaGate3Integration.SuggestionOnly,
                false,
                evidence.HighConfidenceError
                    ? "Suggestion evidence exists, but high-confidence errors prevent AUTO."
                    : "Suggestion evidence exists, but AUTO is not accepted or reliable.");
        }

        return new LayaGate3Decision(
            LayaGate3Status.Fail,
            LayaGate3Integration.No,
            false,
            "Quality, calibration, performance, deployment, or suggestion evidence is insufficient.");
    }
}
