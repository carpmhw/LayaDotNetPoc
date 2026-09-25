using Laya.Core.Exceptions;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.Tokenization;

namespace Laya.Core.Tests.Inference;

[Trait("Category", "EnglishModel")]
public sealed class LayaDecisionEngineTests
{
    /// <summary>驗證多題 request 只建立一次 engine 並回傳保序校準結果。</summary>
    [Fact]
    public void Decide_ReturnsOrderedChoiceAndNoulAnswers()
    {
        var modelRoot = FindModelRoot();
        using var engine = LayaDecisionEngine.Open(modelRoot);
        var result = engine.Decide(
            new LayaRequest(
                "merchant state",
                new[]
                {
                    new LayaQuestion("category", LayaQuestionType.Choice, "Pick one", new[] { "food", "travel" }),
                    new LayaQuestion("needs_review", LayaQuestionType.Noul, "Is this uncertain?")
                }));

        Assert.Equal(new[] { "category", "needs_review" }, result.Answers.Select(answer => answer.Name));
        Assert.True(result.InferenceDuration > TimeSpan.Zero);
        Assert.All(result.Answers, answer =>
        {
            Assert.InRange(answer.Probability, 0d, 1d);
            Assert.Equal(1d, answer.Probabilities.Values.Sum(), precision: 6);
        });
        Assert.Equal(2, result.Answers[0].Probabilities.Count);
        Assert.Equal(2, result.Answers[1].Probabilities.Count);
        Assert.InRange(result.Answers[0].Probabilities["food"], 0.3614, 0.3616);
        Assert.InRange(result.Answers[0].Probabilities["travel"], 0.6384, 0.6386);
        Assert.InRange(result.Answers[1].Probability, 0.1189, 0.1191);

        var repeated = engine.Decide(
            new LayaRequest(
                "merchant state",
                new[]
                {
                    new LayaQuestion("category", LayaQuestionType.Choice, "Pick one", new[] { "food", "travel" }),
                    new LayaQuestion("needs_review", LayaQuestionType.Noul, "Is this uncertain?")
                }));

        Assert.Equal(
            result.Answers.Select(answer => answer.SelectedOption),
            repeated.Answers.Select(answer => answer.SelectedOption));
    }

    /// <summary>驗證空 questions 在建立 ONNX tensors 前會被拒絕。</summary>
    [Fact]
    public void Decide_RejectsEmptyQuestions()
    {
        var modelRoot = FindModelRoot();
        using var engine = LayaDecisionEngine.Open(modelRoot);

        Assert.Throws<LayaConfigurationException>(
            () => engine.Decide(new LayaRequest("state", Array.Empty<LayaQuestion>())));
    }

    /// <summary>驗證 duplicate question names 不會造成結果映射歧義。</summary>
    [Fact]
    public void Decide_RejectsDuplicateQuestionNames()
    {
        var modelRoot = FindModelRoot();
        using var engine = LayaDecisionEngine.Open(modelRoot);
        var questions = new[]
        {
            new LayaQuestion("same", LayaQuestionType.Choice, "Pick", new[] { "a", "b" }),
            new LayaQuestion("same", LayaQuestionType.Choice, "Pick", new[] { "a", "b" })
        };

        Assert.Throws<LayaConfigurationException>(
            () => engine.Decide(new LayaRequest("state", questions)));
    }

    /// <summary>驗證目前 Score 尚未配置 tensors 時會明確拒絕。</summary>
    [Fact]
    public void Decide_RejectsUnsupportedScore()
    {
        var modelRoot = FindModelRoot();
        using var engine = LayaDecisionEngine.Open(modelRoot);
        var request = new LayaRequest(
            "state",
            new[] { new LayaQuestion("score", LayaQuestionType.Score, "Rate", new[] { "low", "high" }) });

        Assert.Throws<LayaConfigurationException>(() => engine.Decide(request));
    }

    /// <summary>驗證長生命週期 engine 重用注入資源並於重複 Dispose 時釋放 owner。</summary>
    [Fact]
    public void Dispose_ReleasesInjectedSessionAndTokenizerAfterRepeatedDecisions()
    {
        var modelRoot = FindModelRoot();
        var session = LayaOnnxSession.Open(modelRoot);
        var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var engine = new LayaDecisionEngine(session, tokenizer);
        var request = new LayaRequest(
            "synthetic shared session ownership",
            new[] { new LayaQuestion("category", LayaQuestionType.Choice, "Pick one", new[] { "food", "travel" }) });

        _ = engine.Decide(request);
        _ = engine.Decide(request);
        engine.Dispose();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = session.OrtSession;
        });
        var tokenizationException = Assert.Throws<LayaTokenizationException>(() =>
        {
            _ = tokenizer.Encode("synthetic state");
        });
        Assert.IsType<ObjectDisposedException>(tokenizationException.InnerException);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = engine.LoadDuration;
        });
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
