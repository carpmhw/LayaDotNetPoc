using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class LayaCalibrationAnalyzerTests
{
    /// <summary>驗證 confidence 邊界、十等寬 ECE 與空桶語意。</summary>
    [Fact]
    public void Analyze_UsesClosedOpenBucketsAndHandComputableEce()
    {
        var samples = new[]
        {
            CreatePrediction("1", "food", "food", 0.49, 0.05),
            CreatePrediction("2", "food", "other", 0.50, 0.05),
            CreatePrediction("3", "food", "food", 0.99, 0.20),
            CreatePrediction("4", "food", "other", 1.00, 0.90)
        };

        var report = LayaCalibrationAnalyzer.Analyze(samples);

        Assert.Equal(4, report.SuccessCount);
        Assert.Equal(4, report.ConfidenceBuckets.Sum(bucket => bucket.Count));
        Assert.Equal(1, report.ConfidenceBuckets.Single(bucket => bucket.LowerBound == 0).Count);
        Assert.Equal(1, report.ConfidenceBuckets.Single(bucket => bucket.LowerBound == 0.5).Count);
        Assert.Equal(0, report.ConfidenceBuckets.Single(bucket => bucket.LowerBound == 0.95).Count);
        Assert.Equal(2, report.ConfidenceBuckets.Single(bucket => bucket.LowerBound == 0.99).Count);
        Assert.Equal(0.5, report.Ece!.Value, precision: 12);
        Assert.Equal(2, report.MarginBuckets.Single(bucket => bucket.LowerBound == 0).Count);
        Assert.Equal(1, report.MarginBuckets.Single(bucket => bucket.LowerBound == 0.8).Count);
    }

    /// <summary>驗證沒有成功樣本時 ECE 與所有空桶 accuracy 為 N/A。</summary>
    [Fact]
    public void Analyze_ReturnsNaForEmptyInput()
    {
        var report = LayaCalibrationAnalyzer.Analyze(Array.Empty<LayaPhase2Prediction>());

        Assert.Null(report.Ece);
        Assert.All(report.EceBins, bucket => Assert.Equal(0, bucket.Count));
        Assert.All(report.EceBins, bucket => Assert.Null(bucket.Accuracy));
    }

    /// <summary>建立最小完整 prediction 供 calibration 手算測試使用。</summary>
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
            0.2,
            null,
            null);
    }
}
