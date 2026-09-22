namespace Laya.Core.Configuration;

/// <summary>保存 POC 使用的模型根目錄與 runtime 選項。</summary>
public sealed record LayaOptions
{
    /// <summary>建立 Laya 選項；未指定時使用 repository 的 models/laya。</summary>
    public LayaOptions(string? modelRoot = null)
    {
        ModelRoot = Path.GetFullPath(modelRoot ?? Path.Combine("models", "laya"));
    }

    /// <summary>取得模型 bundle 的絕對根目錄。</summary>
    public string ModelRoot { get; }
}
