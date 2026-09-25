namespace Laya.MemoryBenchmarks.Orchestration;

/// <summary>保存一個獨立 Probe 子程序的固定 scenario／workload 組態。</summary>
internal sealed class MemoryProbeInvocation
{
    /// <summary>建立一個不可與其他 child run 混淆的 invocation descriptor。</summary>
    public MemoryProbeInvocation(
        string campaignId,
        string stepId,
        string runId,
        string scenario,
        int requests,
        int concurrency,
        bool cpuArenaEnabled,
        string stateProfile,
        int questionCount,
        string workloadId,
        string mode,
        string idleSeconds,
        string? stateSchedule = null,
        string? gcCheckpoints = null,
        string? replicationGroup = null,
        int replicateNumber = 0)
    {
        CampaignId = campaignId;
        StepId = stepId;
        RunId = runId;
        Scenario = scenario;
        Requests = requests;
        Concurrency = concurrency;
        CpuArenaEnabled = cpuArenaEnabled;
        StateProfile = stateProfile;
        QuestionCount = questionCount;
        WorkloadId = workloadId;
        Mode = mode;
        IdleSeconds = idleSeconds;
        StateSchedule = stateSchedule;
        GcCheckpoints = gcCheckpoints;
        ReplicationGroup = replicationGroup;
        ReplicateNumber = replicateNumber;
    }

    /// <summary>取得 campaign ID。</summary>
    public string CampaignId { get; }

    /// <summary>取得 stable campaign step ID。</summary>
    public string StepId { get; }

    /// <summary>取得唯一 run ID。</summary>
    public string RunId { get; }

    /// <summary>取得 Probe scenario CLI name。</summary>
    public string Scenario { get; }

    /// <summary>取得 Phase 3A 固定 multilingual profile。</summary>
    public string ProfileName => "multilingual";

    /// <summary>取得 measured request／session cycle count。</summary>
    public int Requests { get; }

    /// <summary>取得 worker concurrency。</summary>
    public int Concurrency { get; }

    /// <summary>取得 CPU arena requested state。</summary>
    public bool CpuArenaEnabled { get; }

    /// <summary>取得 state profile。</summary>
    public string StateProfile { get; }

    /// <summary>取得每個 request question count。</summary>
    public int QuestionCount { get; }

    /// <summary>取得固定 workload fixture ID。</summary>
    public string WorkloadId { get; }

    /// <summary>取得 pilot／formal mode。</summary>
    public string Mode { get; }

    /// <summary>取得逗號分隔的 post-run idle waits。</summary>
    public string IdleSeconds { get; }

    /// <summary>取得可選的 state schedule。</summary>
    public string? StateSchedule { get; }

    /// <summary>取得可選的 forced-GC checkpoint request counts。</summary>
    public string? GcCheckpoints { get; }

    /// <summary>取得同一 reproduci­bility group ID。</summary>
    public string? ReplicationGroup { get; }

    /// <summary>取得 replicate 序號；非 replicate invocation 為零。</summary>
    public int ReplicateNumber { get; }

    /// <summary>轉換成可傳給 MemoryProbe 的 args，確保組態欄位固定且可重現。</summary>
    public IReadOnlyList<string> ToArguments(
        string outputRoot,
        string? modelRoot = null,
        string? policyPath = null)
    {
        if (Mode == "formal" && policyPath is null)
        {
            throw new ArgumentException("Formal invocations require a frozen policy path.", nameof(policyPath));
        }

        var arguments = new List<string>
        {
            "--scenario", Scenario,
            "--requests", Requests.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--concurrency", Concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--sample-every", "50",
            "--cpu-arena", CpuArenaEnabled ? "on" : "off",
            "--warmup", Scenario == "session-recreate" ? "0" : "5",
            "--questions", QuestionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--workload", WorkloadId,
            "--profile", "multilingual",
            "--mode", Mode,
            "--idle-seconds", IdleSeconds,
            "--run-id", RunId,
            "--output", Path.GetFullPath(outputRoot)
        };

        if (StateSchedule is null)
        {
            arguments.Insert(12, "--state");
            arguments.Insert(13, StateProfile);
        }

        if (modelRoot is not null)
        {
            arguments.Add("--model-root");
            arguments.Add(Path.GetFullPath(modelRoot));
        }

        if (policyPath is not null)
        {
            arguments.Add("--policy");
            arguments.Add(Path.GetFullPath(policyPath));
        }

        if (StateSchedule is not null)
        {
            arguments.Add("--state-schedule");
            arguments.Add(StateSchedule);
        }

        if (GcCheckpoints is not null)
        {
            arguments.Add("--gc-checkpoints");
            arguments.Add(GcCheckpoints);
        }

        if (Mode == "formal" && !IdleSeconds.Split(',', StringSplitOptions.TrimEntries).Contains("300", StringComparer.Ordinal))
        {
            arguments.Add("--idle-omission-reason");
            arguments.Add("This isolated matrix stage does not request a five-minute post-run idle; it is not the baseline soak.");
        }

        return arguments;
    }
}
