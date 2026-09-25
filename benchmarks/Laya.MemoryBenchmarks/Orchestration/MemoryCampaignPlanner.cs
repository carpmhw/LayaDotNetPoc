namespace Laya.MemoryBenchmarks.Orchestration;

/// <summary>建立可重現、依 stage 分離的 Phase 3A probe invocation matrix。</summary>
internal static class MemoryCampaignPlanner
{
    /// <summary>建立超出 tolerance 時使用的下一個獨立 replicate invocation。</summary>
    public static MemoryProbeInvocation CreateThirdReplicate(MemoryProbeInvocation template, int replicateNumber)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.ReplicationGroup is null || template.ReplicateNumber < 1 || replicateNumber < 3)
        {
            throw new ArgumentException("A third replicate requires an existing reproducibility-group invocation.", nameof(template));
        }

        var stepId = $"{template.ReplicationGroup}-r{replicateNumber}";
        return new MemoryProbeInvocation(
            template.CampaignId,
            stepId,
            $"{template.CampaignId}-{stepId}",
            template.Scenario,
            template.Requests,
            template.Concurrency,
            template.CpuArenaEnabled,
            template.StateProfile,
            template.QuestionCount,
            template.WorkloadId,
            template.Mode,
            template.IdleSeconds,
            template.StateSchedule,
            template.GcCheckpoints,
            template.ReplicationGroup,
            replicateNumber);
    }

    /// <summary>依 stage、campaign ID 與 mode 建立無重複 run ID 的 Probe plan。</summary>
    public static IReadOnlyList<MemoryProbeInvocation> CreatePlan(
        string stage,
        string campaignId,
        string mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ValidateCampaignId(campaignId);
        if (mode is not ("pilot" or "formal"))
        {
            throw new ArgumentException("Campaign mode must be pilot or formal.", nameof(mode));
        }

        var plan = stage switch
        {
            "pilot" => CreatePilotPlan(campaignId, mode),
            "baseline" => new[] { FullPipeline(campaignId, "baseline-full-pipeline", 5000, 1, true, "short", 1, mode, "30,60,300") },
            "components" => CreateComponentPlan(campaignId, mode),
            "lifecycle" => new[]
            {
                SessionRecreate(campaignId, 10, mode),
                SessionRecreate(campaignId, 100, mode)
            },
            "arena" => new[]
            {
                FullPipeline(campaignId, "arena-on", 5000, 1, true, "short", 1, mode, "30,60,300"),
                FullPipeline(campaignId, "arena-off", 5000, 1, false, "short", 1, mode, "30,60,300")
            },
            "shape" => CreateShapePlan(campaignId, mode),
            "concurrency" => new[] { 1, 2, 4 }
                .Select(concurrency => FullPipeline(
                    campaignId,
                    $"concurrency-{concurrency}",
                    1000,
                    concurrency,
                    true,
                    "short",
                    2,
                    mode,
                    "0"))
                .ToArray(),
            "reproduction" => CreateReproductionPlan(campaignId, mode),
            _ => throw new ArgumentException($"Unknown memory campaign stage '{stage}'.", nameof(stage))
        };

        if (plan.Select(invocation => invocation.RunId).Distinct(StringComparer.Ordinal).Count() != plan.Length)
        {
            throw new InvalidOperationException("Memory campaign planner generated duplicate run IDs.");
        }

        return plan;
    }

    /// <summary>建立工具低干擾 pilot，不將 pilot 結果視為 formal evidence。</summary>
    private static MemoryProbeInvocation[] CreatePilotPlan(string campaignId, string mode)
    {
        return new[]
        {
            Tokenizer(campaignId, "pilot-tokenizer-en", "tokenizer-en", 1000, mode),
            Tokenizer(campaignId, "pilot-tokenizer-zh", "tokenizer-zh", 1000, mode),
            Tokenizer(campaignId, "pilot-tokenizer-mixed", "tokenizer-mixed", 1000, mode),
            FullPipeline(campaignId, "pilot-full-pipeline", 200, 1, true, "short", 1, mode, "0")
        };
    }

    /// <summary>建立 tokenizer 語言與隔離 component baseline matrix。</summary>
    private static MemoryProbeInvocation[] CreateComponentPlan(string campaignId, string mode)
    {
        var plan = new List<MemoryProbeInvocation>
        {
            Tokenizer(campaignId, "component-tokenizer-en", "tokenizer-en", 10000, mode),
            Tokenizer(campaignId, "component-tokenizer-zh", "tokenizer-zh", 10000, mode),
            Tokenizer(campaignId, "component-tokenizer-mixed", "tokenizer-mixed", 10000, mode),
            Transaction(campaignId, "component-sequence-only", "sequence-only", 10000, mode),
            Transaction(campaignId, "component-tensor-only", "tensor-only", 10000, mode),
            Transaction(campaignId, "component-calibration-only", "calibration-only", 10000, mode, "calibration-mixed-english-reference"),
            Transaction(campaignId, "component-run-only", "run-only", 5000, mode),
            FullPipeline(campaignId, "component-full-pipeline", 5000, 1, true, "short", 1, mode, "30,60,300")
        };
        return plan.ToArray();
    }

    /// <summary>建立 short／medium／long state、question count 與 shape retention matrix。</summary>
    private static MemoryProbeInvocation[] CreateShapePlan(string campaignId, string mode)
    {
        var plan = new List<MemoryProbeInvocation>();
        foreach (var state in new[] { "short", "medium", "long" })
        {
            plan.Add(FullPipeline(campaignId, $"shape-{state}-q1", 1000, 1, true, state, 1, mode, "0"));
        }

        foreach (var questionCount in new[] { 1, 2, 5 })
        {
            plan.Add(FullPipeline(
                campaignId,
                $"shape-question-short-q{questionCount}",
                1000,
                1,
                true,
                "short",
                questionCount,
                mode,
                "0"));
        }

        plan.Add(FullPipeline(
            campaignId,
            "shape-retention-short-long-short",
            3000,
            1,
            true,
            "short",
            1,
            mode,
            "0",
            "short:1000,long:1000,short:1000"));
        return plan.ToArray();
    }

    /// <summary>建立正式 reproducibility matrix 的兩個獨立 replicate。</summary>
    private static MemoryProbeInvocation[] CreateReproductionPlan(string campaignId, string mode)
    {
        var plan = new List<MemoryProbeInvocation>();
        for (var replicate = 1; replicate <= 2; replicate++)
        {
            plan.Add(FullPipeline(campaignId, $"repro-baseline-r{replicate}", 5000, 1, true, "short", 1, mode, "30,60,300"));
            plan.Add(Transaction(campaignId, $"repro-run-only-r{replicate}", "run-only", 5000, mode));
            plan.Add(FullPipeline(campaignId, $"repro-arena-on-r{replicate}", 5000, 1, true, "short", 1, mode, "30,60,300"));
            plan.Add(FullPipeline(campaignId, $"repro-arena-off-r{replicate}", 5000, 1, false, "short", 1, mode, "30,60,300"));
        }

        return plan.ToArray();
    }

    /// <summary>建立單一固定 profile／question full-pipeline invocation。</summary>
    private static MemoryProbeInvocation FullPipeline(
        string campaignId,
        string stepId,
        int requests,
        int concurrency,
        bool cpuArenaEnabled,
        string stateProfile,
        int questionCount,
        string mode,
        string idleSeconds,
        string? stateSchedule = null)
    {
        return CreateInvocation(
            campaignId,
            stepId,
            "full-pipeline",
            requests,
            concurrency,
            cpuArenaEnabled,
            stateProfile,
            questionCount,
            "phase3a-short-transaction",
            mode,
            idleSeconds,
            stateSchedule);
    }

    /// <summary>建立單一 transaction component scenario invocation。</summary>
    private static MemoryProbeInvocation Transaction(
        string campaignId,
        string stepId,
        string scenario,
        int requests,
        string mode,
        string workloadId = "phase3a-short-transaction")
    {
        var questionCount = scenario == "calibration-only" ? 2 : 1;
        return CreateInvocation(
            campaignId,
            stepId,
            scenario,
            requests,
            1,
            true,
            "short",
            questionCount,
            workloadId,
            mode,
            "0");
    }

    /// <summary>建立 tokenizer-only 固定語言 fixture invocation。</summary>
    private static MemoryProbeInvocation Tokenizer(
        string campaignId,
        string stepId,
        string workloadId,
        int requests,
        string mode)
    {
        return CreateInvocation(campaignId, stepId, "tokenizer-only", requests, 1, true, "short", 1, workloadId, mode, "0");
    }

    /// <summary>建立 session recreate cycles invocation。</summary>
    private static MemoryProbeInvocation SessionRecreate(string campaignId, int cycles, string mode)
    {
        return CreateInvocation(
            campaignId,
            $"session-recreate-{cycles}",
            "session-recreate",
            cycles,
            1,
            true,
            "short",
            1,
            "phase3a-short-transaction",
            mode,
            "0");
    }

    /// <summary>建立唯一 run ID 與通用 Probe invocation descriptor。</summary>
    private static MemoryProbeInvocation CreateInvocation(
        string campaignId,
        string stepId,
        string scenario,
        int requests,
        int concurrency,
        bool cpuArenaEnabled,
        string stateProfile,
        int questionCount,
        string workloadId,
        string mode,
        string idleSeconds,
        string? stateSchedule = null)
    {
        var (replicationGroup, replicateNumber) = ParseReplicationGroup(stepId);
        return new MemoryProbeInvocation(
            campaignId,
            stepId,
            $"{campaignId}-{stepId}",
            scenario,
            requests,
            concurrency,
            cpuArenaEnabled,
            stateProfile,
            questionCount,
            workloadId,
            mode,
            idleSeconds,
            stateSchedule,
            replicationGroup: replicationGroup,
            replicateNumber: replicateNumber);
    }

    /// <summary>從 `repro-<group>-rN` step ID 解析重現組及 replicate 序號。</summary>
    private static (string? Group, int Number) ParseReplicationGroup(string stepId)
    {
        if (!stepId.StartsWith("repro-", StringComparison.Ordinal))
        {
            return (null, 0);
        }

        var marker = stepId.LastIndexOf("-r", StringComparison.Ordinal);
        if (marker <= "repro".Length ||
            !int.TryParse(stepId[(marker + 2)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var replicateNumber))
        {
            return (null, 0);
        }

        return (stepId[..marker], replicateNumber);
    }

    /// <summary>驗證 campaign ID 只使用安全 ASCII slug 字元。</summary>
    private static void ValidateCampaignId(string campaignId)
    {
        if (campaignId.Length is < 3 or > 80 ||
            !char.IsAsciiLetterOrDigit(campaignId[0]) ||
            campaignId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new ArgumentException("Campaign ID must be a 3-80 character ASCII slug.", nameof(campaignId));
        }
    }
}
