using Laya.Core.Exceptions;

namespace Laya.Core.Tests.Inference;

public sealed class LayaExceptionTests
{
    /// <summary>驗證六類例外保留 stage 與可用診斷欄位，但 message 不攜帶 inner 原文。</summary>
    [Fact]
    public void Exceptions_ExposeSafeStructuredDiagnostics()
    {
        var inner = new InvalidOperationException("sensitive transaction detail");
        var model = new LayaModelNotFoundException("/models/laya", new[] { "laya.onnx" });
        var configuration = new LayaConfigurationException(
            "invalid question",
            modelPath: "/models/laya",
            questionName: "category",
            optionCount: 11,
            innerException: inner);
        var tokenization = new LayaTokenizationException("tokenizer failed", modelPath: "/models/laya");
        var tooLong = new LayaInputTooLongException("required structure is too long", 512, 11, "category");
        var inference = new LayaInferenceException("run failed", modelPath: "/models/laya", innerException: inner);
        var calibration = new LayaCalibrationException("temperature failed", "category", 11, inner);

        Assert.Equal("model-validation", model.Stage);
        Assert.Equal("configuration", configuration.Stage);
        Assert.Equal("tokenization", tokenization.Stage);
        Assert.Equal("sequence-building", tooLong.Stage);
        Assert.Equal("inference", inference.Stage);
        Assert.Equal("calibration", calibration.Stage);
        Assert.Equal("category", configuration.QuestionName);
        Assert.Equal(11, configuration.OptionCount);
        Assert.Equal(512, tooLong.SequenceLength);
        Assert.Same(inner, inference.InnerException);
        Assert.DoesNotContain(inner.Message, configuration.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(inner.Message, inference.Message, StringComparison.Ordinal);
    }
}
