namespace Laya.MemoryProbe.Configuration;

/// <summary>列出 Phase 3A 可隔離執行的 memory probe scenario。</summary>
internal enum MemoryProbeScenario
{
    /// <summary>只重複 tokenizer encode。</summary>
    TokenizerOnly,

    /// <summary>只建置 sequence batch，不呼叫 ONNX。</summary>
    SequenceOnly,

    /// <summary>只建立並釋放 input tensors。</summary>
    TensorOnly,

    /// <summary>重複固定 tensor 的 ONNX Run。</summary>
    RunOnly,

    /// <summary>只校準固定 raw outputs。</summary>
    CalibrationOnly,

    /// <summary>重複完整 Laya decision pipeline。</summary>
    FullPipeline,

    /// <summary>逐次建立、執行並釋放 ONNX session。</summary>
    SessionRecreate
}

/// <summary>描述一次 memory probe 的執行模式。</summary>
internal enum MemoryProbeMode
{
    /// <summary>只量測工具開銷並建立正式門檻候選。</summary>
    Pilot,

    /// <summary>使用已凍結 policy 執行正式 evidence campaign。</summary>
    Formal
}

/// <summary>保存單一固定 state profile 的 request 數。</summary>
internal sealed class StateScheduleSegment
{
    /// <summary>建立固定 state profile 的 schedule segment。</summary>
    public StateScheduleSegment(string stateProfile, int requests)
    {
        StateProfile = stateProfile;
        Requests = requests;
    }

    /// <summary>取得此區段使用的 state profile。</summary>
    public string StateProfile { get; }

    /// <summary>取得此區段的正式 request 數。</summary>
    public int Requests { get; }
}

/// <summary>保存 CLI 解析及驗證完成的單一 probe 組態。</summary>
internal sealed class MemoryProbeOptions
{
    /// <summary>取得 scenario；load-only 模式沒有 request scenario。</summary>
    public MemoryProbeScenario? Scenario { get; init; }

    /// <summary>取得正式工作量；load-only 模式為零。</summary>
    public int Requests { get; init; }

    /// <summary>取得 worker concurrency。</summary>
    public int Concurrency { get; init; }

    /// <summary>取得週期採樣 request 間隔。</summary>
    public int SampleEvery { get; init; }

    /// <summary>取得是否啟用 CPU memory arena。</summary>
    public bool CpuArenaEnabled { get; init; }

    /// <summary>取得不計入正式 requests 的 warmup 數。</summary>
    public int WarmupRequests { get; init; }

    /// <summary>取得固定 state profile。</summary>
    public string StateProfile { get; init; } = "short";

    /// <summary>取得每一 request 的 question 數。</summary>
    public int QuestionCount { get; init; }

    /// <summary>取得各 idle wait 秒數。</summary>
    public IReadOnlyList<int> IdleSeconds { get; init; } = Array.Empty<int>();

    /// <summary>取得省略 300 秒 idle checkpoint 時的說明。</summary>
    public string? IdleOmissionReason { get; init; }

    /// <summary>取得 forced-GC checkpoint request counts。</summary>
    public IReadOnlyList<int> GcCheckpoints { get; init; } = Array.Empty<int>();

    /// <summary>取得 raw evidence 輸出根目錄。</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>取得此 run 的唯一識別碼。</summary>
    public string RunId { get; init; } = string.Empty;

    /// <summary>取得 host profile 名稱；Phase 3A 固定為 multilingual。</summary>
    public string ProfileName { get; init; } = "multilingual";

    /// <summary>取得可選的 host 已解析 model-root override。</summary>
    public string? ModelRootOverride { get; init; }

    /// <summary>取得 fixture workload ID。</summary>
    public string WorkloadId { get; init; } = string.Empty;

    /// <summary>取得 formal mode 使用的 frozen policy 路徑。</summary>
    public string? PolicyPath { get; init; }

    /// <summary>取得 pilot 或 formal mode。</summary>
    public MemoryProbeMode Mode { get; init; }

    /// <summary>取得是否只載入並驗證模型而不執行 request。</summary>
    public bool IsLoadOnly { get; init; }

    /// <summary>取得可選的固定 state profile schedule。</summary>
    public IReadOnlyList<StateScheduleSegment> StateSchedule { get; init; } = Array.Empty<StateScheduleSegment>();
}
