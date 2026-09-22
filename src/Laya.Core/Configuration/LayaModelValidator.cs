using Laya.Core.Exceptions;

namespace Laya.Core.Configuration;

/// <summary>驗證本機 Laya model bundle 的檔案與設定契約。</summary>
public static class LayaModelValidator
{
    /// <summary>驗證必要檔案並載入 laya_config.json。</summary>
    public static LayaModelBundle ValidateBundle(string modelRoot)
    {
        var root = Path.GetFullPath(modelRoot);
        var requiredFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["laya.onnx"] = Path.Combine(root, "laya.onnx"),
            ["laya.onnx.data"] = Path.Combine(root, "laya.onnx.data"),
            ["laya_config.json"] = Path.Combine(root, "laya_config.json"),
            ["tokenizer/tokenizer.json"] = Path.Combine(root, "tokenizer", "tokenizer.json"),
            ["tokenizer/tokenizer_config.json"] = Path.Combine(root, "tokenizer", "tokenizer_config.json")
        };

        var missingFiles = requiredFiles
            .Where(item => !File.Exists(item.Value))
            .Select(item => item.Key)
            .ToArray();

        if (missingFiles.Length > 0)
        {
            throw new LayaModelNotFoundException(root, missingFiles);
        }

        var config = LayaModelConfig.Load(requiredFiles["laya_config.json"]);

        return new LayaModelBundle(
            root,
            requiredFiles["laya.onnx"],
            requiredFiles["laya.onnx.data"],
            requiredFiles["laya_config.json"],
            requiredFiles["tokenizer/tokenizer.json"],
            requiredFiles["tokenizer/tokenizer_config.json"],
            config);
    }
}
