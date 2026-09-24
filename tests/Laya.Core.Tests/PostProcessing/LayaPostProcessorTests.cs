using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.PostProcessing;
using Laya.Core.Tokenization;

namespace Laya.Core.Tests.PostProcessing;

[Trait("Category", "PureLogic")]
public sealed class LayaPostProcessorTests
{
    /// <summary>驗證 synthetic logits 會依 option 順序映射成 Choice answer。</summary>
    [Fact]
    public void Process_MapsChoiceProbabilitiesInOptionOrder()
    {
        var modelRoot = FindModelRoot();
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        var request = new LayaRequest(
            "state",
            new[] { new LayaQuestion("category", LayaQuestionType.Choice, "Pick", new[] { "a", "b" }) });
        var batch = new LayaSequenceBuilder(tokenizer, config).Build(request);
        var raw = new LayaInferenceResult(new[] { 1f, 3f }, new[] { 0.4f, 0.6f }, TimeSpan.FromMilliseconds(2));

        var answers = new LayaPostProcessor().Process(request, batch, raw, config);

        Assert.Equal("b", answers[0].SelectedOption);
        Assert.Equal(2, answers[0].Probabilities.Count);
        Assert.Equal(1d, answers[0].Probabilities.Values.Sum(), precision: 12);
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
