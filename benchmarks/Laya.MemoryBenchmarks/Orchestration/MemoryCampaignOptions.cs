using Laya.MemoryBenchmarks.Analysis;

namespace Laya.MemoryBenchmarks.Orchestration;

/// <summary>保存已驗證 memory campaign CLI options 與 optional frozen policy。</summary>
internal sealed class MemoryCampaignOptions
{
    /// <summary>取得要執行的可分段 campaign stage。</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>取得 campaign 的唯一識別碼。</summary>
    public string CampaignId { get; init; } = string.Empty;

    /// <summary>取得 pilot 或 formal mode。</summary>
    public string Mode { get; init; } = "pilot";

    /// <summary>取得累積 per-run evidence 的 output root。</summary>
    public string OutputRoot { get; init; } = string.Empty;

    /// <summary>取得 optional verified model root override。</summary>
    public string? ModelRoot { get; init; }

    /// <summary>取得 optional frozen policy file path。</summary>
    public string? PolicyPath { get; init; }

    /// <summary>取得是否允許驗證並 reuse matching complete runs。</summary>
    public bool Resume { get; init; }

    /// <summary>取得完成 semantic validation 的 frozen formal policy。</summary>
    public FrozenMemoryPolicy? Policy { get; init; }
}
