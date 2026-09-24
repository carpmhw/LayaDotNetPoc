using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class LayaPolicyEvaluatorTests
{
    /// <summary>驗證 9x8 grid、AND 條件、全輸入 coverage 與空 qualifying subset。</summary>
    [Fact]
    public void EvaluateGrid_ProducesAllThresholdPairsAndUsesAndCondition()
    {
        var samples = new[]
        {
            CreatePrediction("1", "food", "food", 0.95, 0.30),
            CreatePrediction("2", "food", "food", 0.95, 0.05)
        };

        var report = LayaPolicyEvaluator.EvaluateGrid(samples, inputCount: 3);

        Assert.Equal(72, report.Cells.Count);
        var qualifying = report.Cells.Single(cell => cell.ProbabilityThreshold == 0.90 && cell.MarginThreshold == 0.20);
        Assert.Equal(1, qualifying.QualifiedCount);
        Assert.Equal(1d / 3, qualifying.Coverage, precision: 12);
        Assert.Equal(1d, qualifying.Accuracy!.Value, precision: 12);
        Assert.Equal(0, qualifying.WrongCount);
        var empty = report.Cells.Single(cell => cell.ProbabilityThreshold == 0.99 && cell.MarginThreshold == 0.80);
        Assert.Equal(0, empty.QualifiedCount);
        Assert.Equal(0d, empty.Coverage);
        Assert.Null(empty.Accuracy);
    }

    /// <summary>驗證 conservative／balanced 未達 target 時停用 AUTO，不降低 target。</summary>
    [Fact]
    public void SelectScenarios_DisablesAutoWhenTargetIsNotMet()
    {
        var samples = new[]
        {
            CreatePrediction("1", "food", "food", 0.80, 0.20),
            CreatePrediction("2", "food", "other", 0.80, 0.20)
        };
        var grid = LayaPolicyEvaluator.EvaluateGrid(samples, inputCount: 2);

        var conservative = LayaPolicyEvaluator.SelectScenario(grid, LayaPolicyScenario.Conservative);
        var balanced = LayaPolicyEvaluator.SelectScenario(grid, LayaPolicyScenario.Balanced);
        var aggressive = LayaPolicyEvaluator.SelectScenario(grid, LayaPolicyScenario.Aggressive);

        Assert.False(conservative.AutoEnabled);
        Assert.False(balanced.AutoEnabled);
        Assert.Null(conservative.SelectedCell);
        Assert.True(aggressive.AutoEnabled);
        Assert.NotNull(aggressive.SelectedCell);
    }

    /// <summary>驗證 policy 依 AUTO→SUGGEST→REVIEW 分類，Noul 不會偷渡 override。</summary>
    [Fact]
    public void Classify_UsesExplicitPolicyAndRequiresBothAutoThresholds()
    {
        var policy = new LayaPhase2Policy(
            "balanced",
            AutoEnabled: true,
            AutoProbabilityThreshold: 0.90,
            AutoMarginThreshold: 0.30,
            SuggestProbabilityThreshold: 0.60,
            SuggestMarginThreshold: 0.10);

        Assert.Equal(
            LayaDecisionMode.Auto,
            LayaPolicyEvaluator.Classify(CreatePrediction("1", "food", "food", 0.95, 0.30), policy));
        Assert.Equal(
            LayaDecisionMode.Suggest,
            LayaPolicyEvaluator.Classify(CreatePrediction("2", "food", "food", 0.95, 0.20), policy));
        Assert.Equal(
            LayaDecisionMode.Review,
            LayaPolicyEvaluator.Classify(CreatePrediction("3", "food", "food", 0.99, 0.05), policy));
        Assert.Equal(LayaDecisionMode.Review, LayaPolicyEvaluator.ClassifyFailure(policy));
    }

    /// <summary>建立 policy evaluator 使用的最小完整 prediction。</summary>
    private static LayaPhase2Prediction CreatePrediction(
        string id,
        string expected,
        string predicted,
        double probability,
        double margin)
    {
        var probabilities = LayaPhase2Labels.Categories.ToDictionary(
            label => label,
            label => label == predicted ? probability : (1 - probability) / 10,
            StringComparer.Ordinal);
        return new LayaPhase2Prediction(
            id,
            expected,
            predicted,
            "en",
            probabilities,
            LayaPhase2Labels.Categories,
            probability,
            margin,
            0.8,
            null,
            null);
    }
}
