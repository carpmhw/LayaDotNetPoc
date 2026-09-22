using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.Tokenization;

namespace Laya.Core.Tests.Inference;

public sealed class LayaInferenceRunnerTests
{
    /// <summary>驗證五項 OrtValue input 可透過單次 CPU Run 取得 raw outputs。</summary>
    [Fact]
    public void Run_ReturnsExpectedOutputShapesAndFiniteValues()
    {
        var modelRoot = FindModelRoot();
        using var session = LayaOnnxSession.Open(modelRoot);
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        var batch = new LayaSequenceBuilder(tokenizer, config).Build(
            new LayaRequest(
                "merchant state",
                new[]
                {
                    new LayaQuestion("category", LayaQuestionType.Choice, "Pick one", new[] { "food", "travel" }),
                    new LayaQuestion("needs_review", LayaQuestionType.Noul, "Is this uncertain?")
                }));

        var result = new LayaInferenceRunner(session).Run(batch);

        Assert.Equal(batch.BatchSize * batch.MarkerCount, result.Logits.Length);
        Assert.Equal(batch.BatchSize * 2, result.ActProbabilities.Length);
        Assert.True(result.RunDuration > TimeSpan.Zero);
        Assert.All(result.Logits, value => Assert.True(float.IsFinite(value)));
        Assert.All(result.ActProbabilities, value => Assert.True(float.IsFinite(value)));
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
