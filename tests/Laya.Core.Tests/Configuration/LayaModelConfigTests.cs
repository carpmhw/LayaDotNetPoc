using Laya.Core.Configuration;
using Laya.Core.Exceptions;

namespace Laya.Core.Tests.Configuration;

[Trait("Category", "PureLogic")]
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

    /// <summary>驗證 multilingual fallback calibration 可在沒有 cardinality map 時使用。</summary>
    [Fact]
    public void GetTemperature_AllowsEmptyOptionCalibrationWithFallbacks()
    {
        var config = new LayaModelConfig
        {
            MaxLength = 1024,
            HeadMaxLength = 256,
            Temperature = new[] { 1d, 1d, 1d },
            TemperatureByOptions = new Dictionary<string, double>()
        };

        Assert.Equal(1d, config.GetTemperature("choice", 11));
        Assert.Equal(1d, config.GetTemperature("noul", 2));
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
