namespace Laya.Core.Configuration;

/// <summary>保存 POC 使用的模型根目錄與 runtime 選項。</summary>
public sealed record LayaOptions
{
    /// <summary>建立由 host 解析完成的 Laya 選項，不在 Core 內推測模型路徑。</summary>
    public LayaOptions(
        string modelRoot,
        string? profile = null,
        string? checkpointRevision = null,
        string? manifestPath = null,
        bool allowCandidateStaged = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelRoot);
        ModelRoot = Path.GetFullPath(modelRoot);
        Profile = profile;
        CheckpointRevision = checkpointRevision;
        ManifestPath = manifestPath is null ? null : Path.GetFullPath(manifestPath);
        AllowCandidateStaged = allowCandidateStaged;
    }

    /// <summary>取得模型 bundle 的絕對根目錄。</summary>
    public string ModelRoot { get; }

    /// <summary>取得 host 解析的 profile 名稱。</summary>
    public string? Profile { get; }

    /// <summary>取得 host 解析的 checkpoint revision。</summary>
    public string? CheckpointRevision { get; }

    /// <summary>取得可選的 bundle manifest 絕對路徑。</summary>
    public string? ManifestPath { get; }

    /// <summary>取得是否由 parity harness 明確允許載入 candidate-staged bundle。</summary>
    public bool AllowCandidateStaged { get; }
}
