using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class LayaNoulAnalyzerTests
{
    /// <summary>驗證五個 Noul bucket、分類 accuracy 與正確／錯誤平均 P(true)。</summary>
    [Fact]
    public void Analyze_ReportsFiveBucketsAndCorrectWrongMeans()
    {
        var samples = new[]
        {
            CreatePrediction("1", "food", "food", 0.10),
            CreatePrediction("2", "food", "other", 0.20),
            CreatePrediction("3", "food", "food", 0.40),
            CreatePrediction("4", "food", "other", 0.60),
            CreatePrediction("5", "food", "food", 0.80)
        };

        var report = LayaNoulAnalyzer.Analyze(samples);

        Assert.Equal(5, report.Buckets.Count);
        Assert.Equal(1, report.Buckets[0].Count);
        Assert.Equal(1, report.Buckets[1].Count);
        Assert.Equal(1, report.Buckets[2].Count);
        Assert.Equal(1, report.Buckets[3].Count);
        Assert.Equal(1, report.Buckets[4].Count);
        Assert.Equal(1d, report.Buckets[0].Accuracy!.Value, precision: 12);
        Assert.Equal(0d, report.Buckets[1].Accuracy!.Value, precision: 12);
        Assert.Equal(0.10, report.Buckets[0].CorrectMean!.Value, precision: 12);
        Assert.Equal(0.20, report.Buckets[1].WrongMean!.Value, precision: 12);
    }

    /// <summary>驗證空 bucket 的 accuracy 與平均值均為 N/A。</summary>
    [Fact]
    public void Analyze_ReturnsNaForEmptyBuckets()
    {
        var report = LayaNoulAnalyzer.Analyze(new[]
        {
            CreatePrediction("1", "food", "food", 0.80)
        });

        Assert.All(report.Buckets.Take(4), bucket =>
        {
            Assert.Null(bucket.Accuracy);
            Assert.Null(bucket.CorrectMean);
            Assert.Null(bucket.WrongMean);
        });
    }

    /// <summary>建立 Noul 分析使用的 prediction。</summary>
    private static LayaPhase2Prediction CreatePrediction(
        string id,
        string expected,
        string predicted,
        double noulProbability)
    {
        var probabilities = LayaPhase2Labels.Categories.ToDictionary(
            label => label,
            label => label == predicted ? 0.8 : 0.2 / 10,
            StringComparer.Ordinal);
        return new LayaPhase2Prediction(
            id,
            expected,
            predicted,
            "en",
            probabilities,
            LayaPhase2Labels.Categories,
            0.8,
            0.5,
            noulProbability,
            null,
            null);
    }
}
