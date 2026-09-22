using Laya.Core.Configuration;
using Laya.Core.Exceptions;

namespace Laya.Core.Tests.Configuration;

public sealed class LayaModelConfigTests
{
    /// <summary>驗證真實 bundle 的 per-cardinality temperature lookup。</summary>
    [Fact]
    public void GetTemperature_UsesReferenceCardinalityBuckets()
    {
        var modelRoot = FindModelRoot();
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));

        Assert.Equal(1.9063563346862793, config.GetTemperature("choice", 2), precision: 12);
        Assert.Equal(1.7601518630981445, config.GetTemperature("choice", 4), precision: 12);
        Assert.Equal(1.983399510383606, config.GetTemperature("noul", 2), precision: 12);
    }

    /// <summary>驗證未知 question type 不會套用猜測的 fallback temperature。</summary>
    [Fact]
    public void GetTemperature_RejectsUnknownQuestionType()
    {
        var config = new LayaModelConfig
        {
            MaxLength = 512,
            HeadMaxLength = 192,
            Temperature = new[] { 1d, 1d, 1d },
            TemperatureByOptions = new Dictionary<string, double> { ["choice:2"] = 1d }
        };

        Assert.Throws<LayaConfigurationException>(
            () => config.GetTemperature("unknown", 2));
    }

    /// <summary>尋找 repository 內的本機模型目錄，允許 CI 透過環境變數覆寫。</summary>
    private static string FindModelRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LAYA_MODEL_ROOT");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../models/laya"));
    }
}
