namespace Laya.MemoryBenchmarks.Analysis;

/// <summary>列出正式 Phase 3A memory diagnostic 的五種互斥分類。</summary>
internal enum MemoryDiagnosticClassification
{
    /// <summary>目前 evidence 未顯示 memory leak。</summary>
    NoLeakEvidence,

    /// <summary>量測顯示 bounded allocator／arena plateau。</summary>
    AllocatorPlateau,

    /// <summary>有 native retention，但 source 尚未確認。</summary>
    NativeRetentionSuspected,

    /// <summary>固定 workload 呈現未 plateau 的持續成長。</summary>
    LeakSuspected,

    /// <summary>重現與 lifecycle audit 均確認未釋放資源。</summary>
    LeakConfirmed
}

/// <summary>區分 evidence 是否完整與 Memory Gate 本身的結果。</summary>
internal enum MemoryEvidenceStatus
{
    /// <summary>必要 experiments 和身分證據均齊備。</summary>
    Complete,

    /// <summary>缺 runs、thresholds、hashes 或可用 metrics。</summary>
    Blocked
}

/// <summary>表示可計算的 gate outcome；缺資料時整體 outcome 為 null。</summary>
internal enum MemoryGateStatus
{
    /// <summary>符合 memory readiness 所有必要條件。</summary>
    Pass,

    /// <summary>僅允許在明列 deployment 條件下有條件繼續。</summary>
    Partial,

    /// <summary>存在持續 leak 或 target deployment failure。</summary>
    Fail
}

/// <summary>保存 Memory Gate 可重算所需的必要 run facts。</summary>
internal sealed record MemoryGateEvidence
{
    /// <summary>取得必需 reports、runs、hashes 與 metrics 是否完整。</summary>
    public bool EvidenceComplete { get; init; }

    /// <summary>取得完整 full-pipeline measured request 數。</summary>
    public int FullPipelineRequests { get; init; }

    /// <summary>取得 full-pipeline crash 數。</summary>
    public int CrashCount { get; init; }

    /// <summary>取得 OOM events 數。</summary>
    public int OomCount { get; init; }

    /// <summary>取得 request operation error 數。</summary>
    public int Errors { get; init; }

    /// <summary>取得 result integrity failures 數。</summary>
    public int IntegrityFailures { get; init; }

    /// <summary>取得 PrivateMemory late slope／growth 是否有 plateau。</summary>
    public bool PrivateMemoryPlateau { get; init; }

    /// <summary>取得 RSS late slope／growth 是否有 plateau。</summary>
    public bool RssPlateau { get; init; }

    /// <summary>取得 managed heap 是否穩定。</summary>
    public bool ManagedHeapStable { get; init; }

    /// <summary>取得固定 target Docker profile 是否穩定。</summary>
    public bool TargetDockerStable { get; init; }

    /// <summary>取得所需重複 runs 是否一致。</summary>
    public bool ReplicatesConsistent { get; init; }

    /// <summary>取得 target profile 是否發生 repeated OOM。</summary>
    public bool TargetDockerRepeatedOom { get; init; }

    /// <summary>取得同一 workload 是否 persistent near-linear 成長。</summary>
    public bool PersistentNearLinearGrowth { get; init; }

    /// <summary>取得 5,000 requests 後仍沒有 bounded plateau 的診斷。</summary>
    public bool NoPlateauAt5000 { get; init; }

    /// <summary>取得 session recreate 是否呈現 unbounded growth。</summary>
    public bool SessionRecreateUnboundedGrowth { get; init; }

    /// <summary>取得 leak reproduction、retention source 與 audit 的三項 evidence。</summary>
    public bool LeakReproduced { get; init; }

    /// <summary>取得是否找出明確 retention source。</summary>
    public bool RetentionSourceIdentified { get; init; }

    /// <summary>取得 lifecycle audit 是否證明未 Dispose resource。</summary>
    public bool LifecycleAuditConfirmsUndisposedResource { get; init; }

    /// <summary>取得 native retention 是否被觀察到。</summary>
    public bool NativeRetentionObserved { get; init; }

    /// <summary>取得 allocator comparison 是否支持 plateau 歸因。</summary>
    public bool AllocatorComparisonSupportsPlateau { get; init; }

    /// <summary>取得 native retention 是否 bounded 且可解釋。</summary>
    public bool BoundedNativeRetention { get; init; }

    /// <summary>取得 bounded native retention 是否有完整解釋。</summary>
    public bool NativeRetentionExplained { get; init; }

    /// <summary>取得 arena memory／latency trade-off 是否可重現。</summary>
    public bool ArenaTradeoffExplained { get; init; }

    /// <summary>取得下一階段 deployment memory limit 是否明確記錄。</summary>
    public bool DeploymentLimitDocumented { get; init; }

    /// <summary>取得下一階段 memory monitoring conditions 是否明確記錄。</summary>
    public bool MonitoringConditionsDocumented { get; init; }

    /// <summary>取得 WorkingSet peak 是否高於 diagnostic boundary，僅供說明。</summary>
    public bool WorkingSetPeakAboveDiagnosticBoundary { get; init; }
}

/// <summary>保存 evidence status、診斷分類、gate 結果與 integration recommendation。</summary>
internal sealed class MemoryGateDecision
{
    /// <summary>建立完整或 blocked 的 Memory Gate decision。</summary>
    public MemoryGateDecision(
        MemoryEvidenceStatus evidenceStatus,
        MemoryGateStatus? status,
        MemoryDiagnosticClassification? classification,
        string recommendation,
        IReadOnlyList<string> reasons)
    {
        EvidenceStatus = evidenceStatus;
        Status = status;
        Classification = classification;
        Recommendation = recommendation;
        Reasons = reasons;
    }

    /// <summary>取得 evidence complete／blocked 狀態。</summary>
    public MemoryEvidenceStatus EvidenceStatus { get; }

    /// <summary>取得 PASS／PARTIAL／FAIL；blocked evidence 時為 null。</summary>
    public MemoryGateStatus? Status { get; }

    /// <summary>取得五種 memory classification 之一；缺 evidence 時為 null。</summary>
    public MemoryDiagnosticClassification? Classification { get; }

    /// <summary>取得 Phase 3B memory prerequisite recommendation。</summary>
    public string Recommendation { get; }

    /// <summary>取得可讀取的 gate reasons。</summary>
    public IReadOnlyList<string> Reasons { get; }
}

/// <summary>依凍結條件分開判斷診斷分類、evidence completeness 與 Memory Gate。</summary>
internal static class MemoryGateEvaluator
{
    /// <summary>依 fail-first precedence 判定五種 memory classification。</summary>
    public static MemoryDiagnosticClassification? Classify(MemoryGateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.EvidenceComplete)
        {
            return null;
        }

        if (evidence.LeakReproduced && evidence.RetentionSourceIdentified &&
            evidence.LifecycleAuditConfirmsUndisposedResource)
        {
            return MemoryDiagnosticClassification.LeakConfirmed;
        }

        if (evidence.PersistentNearLinearGrowth)
        {
            return MemoryDiagnosticClassification.LeakSuspected;
        }

        if (evidence.NativeRetentionObserved)
        {
            return MemoryDiagnosticClassification.NativeRetentionSuspected;
        }

        if (evidence.PrivateMemoryPlateau && evidence.RssPlateau && evidence.AllocatorComparisonSupportsPlateau)
        {
            return MemoryDiagnosticClassification.AllocatorPlateau;
        }

        if (evidence.FullPipelineRequests >= 5000 &&
            evidence.Errors == 0 && evidence.IntegrityFailures == 0 &&
            evidence.PrivateMemoryPlateau && evidence.RssPlateau && evidence.ManagedHeapStable)
        {
            return MemoryDiagnosticClassification.NoLeakEvidence;
        }

        return null;
    }

    /// <summary>以 PASS／PARTIAL／FAIL precedence 判定 Phase 3B memory readiness。</summary>
    public static MemoryGateDecision Evaluate(MemoryGateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var classification = Classify(evidence);
        if (!evidence.EvidenceComplete)
        {
            return Blocked(classification, "Required Phase 3A evidence, frozen thresholds or metrics are missing.");
        }

        var failReasons = GetFailureReasons(evidence, classification);
        if (failReasons.Count > 0)
        {
            return new MemoryGateDecision(
                MemoryEvidenceStatus.Complete,
                MemoryGateStatus.Fail,
                classification,
                "DO NOT PROCEED",
                failReasons);
        }

        var pass = evidence.FullPipelineRequests >= 5000 &&
            evidence.CrashCount == 0 && evidence.OomCount == 0 && evidence.Errors == 0 &&
            evidence.IntegrityFailures == 0 && evidence.PrivateMemoryPlateau && evidence.RssPlateau &&
            evidence.ManagedHeapStable && evidence.TargetDockerStable && evidence.ReplicatesConsistent;
        if (pass)
        {
            return new MemoryGateDecision(
                MemoryEvidenceStatus.Complete,
                MemoryGateStatus.Pass,
                classification,
                "PROCEED",
                Array.Empty<string>());
        }

        var boundedConditional =
            (evidence.BoundedNativeRetention && evidence.NativeRetentionExplained || evidence.ArenaTradeoffExplained) &&
            evidence.DeploymentLimitDocumented && evidence.MonitoringConditionsDocumented && evidence.ReplicatesConsistent;
        if (boundedConditional)
        {
            return new MemoryGateDecision(
                MemoryEvidenceStatus.Complete,
                MemoryGateStatus.Partial,
                classification,
                "PROCEED WITH CONDITIONS",
                new[] { "Retain the documented deployment memory limit and monitoring conditions." });
        }

        return Blocked(classification, "Complete evidence does not satisfy PASS or bounded PARTIAL criteria.");
    }

    /// <summary>建立 DO NOT PROCEED blocked decision 並保留 unavailable rationale。</summary>
    private static MemoryGateDecision Blocked(MemoryDiagnosticClassification? classification, string reason)
    {
        return new MemoryGateDecision(
            MemoryEvidenceStatus.Blocked,
            null,
            classification,
            "DO NOT PROCEED",
            new[] { reason });
    }

    /// <summary>檢查必須先於任何 PASS／PARTIAL 的 gate failure conditions。</summary>
    private static List<string> GetFailureReasons(
        MemoryGateEvidence evidence,
        MemoryDiagnosticClassification? classification)
    {
        var reasons = new List<string>();
        if (evidence.CrashCount > 0)
        {
            reasons.Add("The full-pipeline or target deployment run crashed.");
        }

        if (evidence.OomCount > 0 || evidence.TargetDockerRepeatedOom)
        {
            reasons.Add("The target Docker memory profile reported OOM.");
        }

        if (evidence.PersistentNearLinearGrowth || evidence.NoPlateauAt5000)
        {
            reasons.Add("PrivateMemory/RSS growth remains unbounded without a late plateau.");
        }

        if (evidence.SessionRecreateUnboundedGrowth)
        {
            reasons.Add("Session recreation caused unbounded process memory growth.");
        }

        if (classification == MemoryDiagnosticClassification.LeakConfirmed)
        {
            reasons.Add("A reproducible undisposed native resource was confirmed by lifecycle audit.");
        }

        if (evidence.FullPipelineRequests >= 5000 && (evidence.Errors > 0 || evidence.IntegrityFailures > 0))
        {
            reasons.Add("The 5,000-request full-pipeline soak contains errors or integrity failures.");
        }

        return reasons;
    }
}
