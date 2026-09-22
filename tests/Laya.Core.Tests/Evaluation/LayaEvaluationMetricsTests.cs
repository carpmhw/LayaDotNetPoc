using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

public sealed class LayaEvaluationMetricsTests
{
    /// <summary>驗證 accuracy、precision、recall、F1 與 threshold coverage 可手算重現。</summary>
    [Fact]
    public void Calculate_ReturnsClassificationAndCoverageMetrics()
    {
        var samples = new[]
        {
            new LayaEvaluationSample("1", "food", "food", 0.90, 0.30, "en"),
            new LayaEvaluationSample("2", "food", "retail", 0.70, 0.10, "en"),
            new LayaEvaluationSample("3", "retail", "retail", 0.80, 0.40, "zh"),
            new LayaEvaluationSample("4", "other", "food", 0.55, 0.05, "zh")
        };

        var report = LayaEvaluationMetrics.Calculate(samples, new[] { "food", "retail", "other" });

        Assert.Equal(0.5, report.OverallAccuracy, precision: 12);
        Assert.Equal(2, report.PerClass["food"].Support);
        Assert.Equal(0.5, report.PerClass["food"].Recall!.Value, precision: 12);
        Assert.Equal(0.5, report.PerClass["food"].Precision!.Value, precision: 12);
        Assert.Equal(0.5, report.PerClass["food"].F1!.Value, precision: 12);
        Assert.Equal(0.75, report.Thresholds[0.60].Coverage, precision: 12);
        Assert.Equal(2d / 3, report.Thresholds[0.60].Accuracy!.Value, precision: 12);
        Assert.Equal(0, report.Thresholds[0.95].IncludedCount);
        Assert.Null(report.Thresholds[0.95].Accuracy);
        Assert.Equal(2, report.ByLanguage["en"].SampleCount);
        Assert.Equal(0.5, report.ByLanguage["zh"].Accuracy, precision: 12);
        Assert.Null(report.PerClass["other"].Precision);
    }

    /// <summary>驗證分母存在但沒有 true positive 時，F1 是 0 而不是 N/A。</summary>
    [Fact]
    public void Calculate_ReturnsZeroF1ForPresentButIncorrectClass()
    {
        var report = LayaEvaluationMetrics.Calculate(
            new[]
            {
                new LayaEvaluationSample("1", "food", "retail", 0.8, 0.3, "en"),
                new LayaEvaluationSample("2", "retail", "food", 0.8, 0.3, "en")
            },
            new[] { "food", "retail" });

        Assert.Equal(0d, report.PerClass["food"].F1!.Value);
        Assert.Equal(0d, report.PerClass["retail"].F1!.Value);
    }
}
