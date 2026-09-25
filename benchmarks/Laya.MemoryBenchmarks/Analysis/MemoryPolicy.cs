namespace Laya.MemoryBenchmarks.Analysis;

/// <summary>保存單一 process-memory metric 的 late slope 與 growth budgets。</summary>
internal sealed class MemorySlopeBudget
{
    /// <summary>建立明確凍結的 slope／growth budgets。</summary>
    public MemorySlopeBudget(double maximumLateSlopeBytesPer100Requests, long lateGrowthBudgetBytes)
    {
        MaximumLateSlopeBytesPer100Requests = maximumLateSlopeBytesPer100Requests;
        LateGrowthBudgetBytes = lateGrowthBudgetBytes;
    }

    /// <summary>取得最大允許 late slope bytes／100 requests。</summary>
    public double MaximumLateSlopeBytesPer100Requests { get; }

    /// <summary>取得 late segment 最大允許 absolute growth bytes。</summary>
    public long LateGrowthBudgetBytes { get; }
}

/// <summary>保存兩次 replicate 差異超界前的相對 tolerance。</summary>
internal sealed class MemoryReplicateTolerance
{
    /// <summary>建立 peak／end／slope relative tolerances。</summary>
    public MemoryReplicateTolerance(double peakRelativeDifferenceMaximum, double endRelativeDifferenceMaximum, double slopeRelativeDifferenceMaximum)
    {
        PeakRelativeDifferenceMaximum = peakRelativeDifferenceMaximum;
        EndRelativeDifferenceMaximum = endRelativeDifferenceMaximum;
        SlopeRelativeDifferenceMaximum = slopeRelativeDifferenceMaximum;
    }

    /// <summary>取得 peak memory 最大 replicate 相對差異。</summary>
    public double PeakRelativeDifferenceMaximum { get; }

    /// <summary>取得 end memory 最大 replicate 相對差異。</summary>
    public double EndRelativeDifferenceMaximum { get; }

    /// <summary>取得 late slope 最大 replicate 相對差異。</summary>
    public double SlopeRelativeDifferenceMaximum { get; }
}

/// <summary>保存正式 Memory Gate 所需的最低實驗矩陣數值。</summary>
internal sealed class MemoryMinimumMatrix
{
    /// <summary>建立凍結的 minimum matrix contract。</summary>
    public MemoryMinimumMatrix(IReadOnlyDictionary<string, object> values)
    {
        Values = values;
    }

    /// <summary>取得已驗證的不可變 minimum matrix scalars 與 lists。</summary>
    public IReadOnlyDictionary<string, object> Values { get; }

    /// <summary>取得 baseline 5,000-request soak 數。</summary>
    public int BaselineRequests => (int)Values["baselineRequests"];

    /// <summary>取得 run-only soak request 數。</summary>
    public int RunOnlyRequests => (int)Values["runOnlyRequests"];

    /// <summary>取得 arena comparison request 數。</summary>
    public int ArenaRequests => (int)Values["arenaRequests"];

    /// <summary>取得 tokenizer 逐語言 request 數。</summary>
    public int TokenizerRequestsPerLanguage => (int)Values["tokenizerRequestsPerLanguage"];
}

/// <summary>保存完成 pilot 並在正式 run 前凍結的 memory acceptance policy。</summary>
internal sealed class FrozenMemoryPolicy
{
    /// <summary>建立已由 loader 驗證且具檔案 SHA-256 的 frozen policy。</summary>
    public FrozenMemoryPolicy(
        string policyId,
        DateTimeOffset frozenUtc,
        string freezeCommit,
        IReadOnlyList<string> pilotRunIds,
        string pilotArtifactSha256,
        long targetMemoryLimitBytes,
        string rationale,
        string limitations,
        MemorySlopeBudget privateMemory,
        MemorySlopeBudget rss,
        MemorySlopeBudget managedHeap,
        double nearLinearR2Minimum,
        MemoryReplicateTolerance replicateTolerance,
        MemoryMinimumMatrix minimumMatrix,
        string sha256)
    {
        PolicyId = policyId;
        FrozenUtc = frozenUtc;
        FreezeCommit = freezeCommit;
        PilotRunIds = pilotRunIds;
        PilotArtifactSha256 = pilotArtifactSha256;
        TargetMemoryLimitBytes = targetMemoryLimitBytes;
        Rationale = rationale;
        Limitations = limitations;
        PrivateMemory = privateMemory;
        Rss = rss;
        ManagedHeap = managedHeap;
        NearLinearR2Minimum = nearLinearR2Minimum;
        ReplicateTolerance = replicateTolerance;
        MinimumMatrix = minimumMatrix;
        Sha256 = sha256;
    }

    /// <summary>取得 frozen policy ID。</summary>
    public string PolicyId { get; }

    /// <summary>取得 policy freeze UTC timestamp。</summary>
    public DateTimeOffset FrozenUtc { get; }

    /// <summary>取得凍結時 source commit。</summary>
    public string FreezeCommit { get; }

    /// <summary>取得用於產生門檻的 pilot run IDs。</summary>
    public IReadOnlyList<string> PilotRunIds { get; }

    /// <summary>取得 pilot artifact aggregate SHA-256。</summary>
    public string PilotArtifactSha256 { get; }

    /// <summary>取得不可由 policy 覆寫的十進位 3 GB target bytes。</summary>
    public long TargetMemoryLimitBytes { get; }

    /// <summary>取得 pilot thresholds 的凍結 rationale。</summary>
    public string Rationale { get; }

    /// <summary>取得 POC policy limitations。</summary>
    public string Limitations { get; }

    /// <summary>取得 PrivateMemory slope／growth budget。</summary>
    public MemorySlopeBudget PrivateMemory { get; }

    /// <summary>取得 RSS slope／growth budget。</summary>
    public MemorySlopeBudget Rss { get; }

    /// <summary>取得 managed heap slope／growth budget。</summary>
    public MemorySlopeBudget ManagedHeap { get; }

    /// <summary>取得 near-linear slope R-squared threshold。</summary>
    public double NearLinearR2Minimum { get; }

    /// <summary>取得 replicate consistency tolerances。</summary>
    public MemoryReplicateTolerance ReplicateTolerance { get; }

    /// <summary>取得 minimum required formal experiment matrix。</summary>
    public MemoryMinimumMatrix MinimumMatrix { get; }

    /// <summary>取得此 policy file 的實際 SHA-256。</summary>
    public string Sha256 { get; }
}
