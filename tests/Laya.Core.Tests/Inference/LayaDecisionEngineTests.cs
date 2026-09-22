using Laya.Core.Exceptions;
using Laya.Core.Inference;
using Laya.Core.Models;

namespace Laya.Core.Tests.Inference;

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
