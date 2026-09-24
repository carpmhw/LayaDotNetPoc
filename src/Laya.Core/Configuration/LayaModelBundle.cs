namespace Laya.Core.Configuration;

/// <summary>保存已驗證的 Laya bundle 路徑與設定。</summary>
public sealed record LayaModelBundle(
    string RootPath,
    string ModelPath,
    string? ExternalDataPath,
    string ConfigPath,
    string TokenizerPath,
    string TokenizerConfigPath,
    LayaModelConfig Config)
{
    /// <summary>取得實際驗證過的所有 external-data shard。</summary>
    public IReadOnlyList<string> ExternalDataPaths { get; init; } =
        ExternalDataPath is null ? Array.Empty<string>() : new[] { ExternalDataPath };

    /// <summary>取得可選的 bundle manifest 路徑。</summary>
    public string? ManifestPath { get; init; }

    /// <summary>取得 manifest 宣告的 profile 身分。</summary>
    public string? Profile { get; init; }

    /// <summary>取得 manifest 宣告的 checkpoint revision。</summary>
    public string? CheckpointRevision { get; init; }

    /// <summary>取得已驗證檔案的相對路徑與 SHA-256。</summary>
    public IReadOnlyDictionary<string, string> VerifiedFiles { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
