namespace Laya.Core.Configuration;

/// <summary>保存已驗證的 Laya bundle 路徑與設定。</summary>
public sealed record LayaModelBundle(
    string RootPath,
    string ModelPath,
    string ExternalDataPath,
    string ConfigPath,
    string TokenizerPath,
    string TokenizerConfigPath,
    LayaModelConfig Config);
