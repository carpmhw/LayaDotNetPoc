using Laya.Core.Configuration;
using Laya.Core.Exceptions;

namespace Laya.Core.Tests.Models;

public sealed class LayaModelValidatorTests
{
    /// <summary>驗證缺少模型資產時會回報缺失檔案。</summary>
    [Fact]
    public void ValidateBundle_ReportsMissingRequiredAsset()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            var exception = Assert.Throws<LayaModelNotFoundException>(
                () => LayaModelValidator.ValidateBundle(directory.FullName));

            Assert.Contains("laya.onnx", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證完整最小 bundle 可以讀取設定與必要路徑。</summary>
    [Fact]
    public void ValidateBundle_LoadsConfigAndPaths()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            CreateMinimalBundle(directory.FullName);

            var bundle = LayaModelValidator.ValidateBundle(directory.FullName);

            Assert.Equal(512, bundle.Config.MaxLength);
            Assert.Equal(192, bundle.Config.HeadMaxLength);
            Assert.Equal(Path.Combine(directory.FullName, "laya.onnx"), bundle.ModelPath);
            Assert.Equal(
                Path.Combine(directory.FullName, "tokenizer", "tokenizer.json"),
                bundle.TokenizerPath);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證無效模型設定不會以預設值靜默通過。</summary>
    [Fact]
    public void ValidateBundle_RejectsInvalidConfig()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            CreateMinimalBundle(directory.FullName);
            File.WriteAllText(
                Path.Combine(directory.FullName, "laya_config.json"),
                "{\"max_len\":0,\"head_max_len\":192,\"temperature\":[1.6,1.2,1.9]," +
                "\"temperature_by_options\":{\"choice:2\":1.9}}");

            Assert.Throws<LayaConfigurationException>(
                () => LayaModelValidator.ValidateBundle(directory.FullName));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>建立供設定驗證使用的最小本機 bundle。</summary>
    private static void CreateMinimalBundle(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "tokenizer"));
        File.WriteAllText(Path.Combine(root, "laya.onnx"), "model");
        File.WriteAllText(Path.Combine(root, "laya.onnx.data"), "weights");
        File.WriteAllText(
            Path.Combine(root, "laya_config.json"),
            "{" +
            "\"max_len\":512," +
            "\"head_max_len\":192," +
            "\"temperature\":[1.6,1.2,1.9]," +
            "\"temperature_by_options\":{\"choice:2\":1.9,\"noul:2\":1.9}" +
            "}");
        File.WriteAllText(Path.Combine(root, "tokenizer", "tokenizer.json"), "{}");
        File.WriteAllText(Path.Combine(root, "tokenizer", "tokenizer_config.json"), "{}");
    }
}
