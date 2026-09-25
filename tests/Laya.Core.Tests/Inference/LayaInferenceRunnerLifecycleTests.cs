using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Exceptions;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.Tokenization;
using Microsoft.ML.OnnxRuntime;

namespace Laya.Core.Tests.Inference;

[Trait("Category", "EnglishModel")]
public sealed class LayaInferenceRunnerLifecycleTests
{
    /// <summary>驗證 native Run 失敗時已建立的五個輸入 OrtValue 都會釋放。</summary>
    [Fact]
    public void Run_DisposesInputValuesWhenNativeRunThrows()
    {
        var modelRoot = FindModelRoot();
        using var session = LayaOnnxSession.Open(modelRoot);
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        var batch = new LayaSequenceBuilder(tokenizer, config).Build(
            new LayaRequest(
                "synthetic memory lifecycle",
                new[] { new LayaQuestion("category", LayaQuestionType.Choice, "Pick one", new[] { "food", "travel" }) }));
        var disposedCount = 0;
        var runner = new LayaInferenceRunner(
            session,
            inputBatch => LayaInputTensorOwner.Create(
                inputBatch,
                CreateValue,
                value =>
                {
                    disposedCount++;
                    value.Dispose();
                }),
            (_, _, _) => throw new InvalidOperationException("synthetic native run failure"));

        var exception = Assert.Throws<LayaInferenceException>(() => runner.Run(batch));

        Assert.Contains("ONNX Runtime inference failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(5, disposedCount);
    }

    /// <summary>依測試輸入陣列型別建立實際 ONNX Runtime tensor value。</summary>
    private static OrtValue CreateValue(Array memory, long[] shape)
    {
        return memory switch
        {
            long[] values => OrtValue.CreateTensorValueFromMemory(values, shape),
            bool[] values => OrtValue.CreateTensorValueFromMemory(values, shape),
            _ => throw new ArgumentException("Unexpected tensor element type.", nameof(memory))
        };
    }

    /// <summary>尋找 repository 內的 English model，允許 CI 透過環境變數覆寫。</summary>
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
