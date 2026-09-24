using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class LayaPhase2MetricsTests
{
    /// <summary>驗證成功與失敗分別使用成功分母及全輸入分母。</summary>
    [Fact]
    public void Calculate_ReportsSuccessAndFullInputAccuracySeparately()
    {
        var predictions = new[]
        {
            CreatePrediction("1", "food", "food", "en", ["food", "transfer", "other"]),
            CreatePrediction("2", "food", "transfer", "en", ["transfer", "food", "other"]),
            CreatePrediction("3", "transfer", "other", "zh", ["other", "transfer", "food"])
        };
        var failures = new[] { new LayaPhase2Failure("4", "inference", "LayaInferenceException", "zh") };

        var report = LayaPhase2Metrics.Calculate(predictions, failures);

        Assert.Equal(4, report.InputCount);
        Assert.Equal(3, report.SuccessCount);
        Assert.Equal(1, report.FailureCount);
        Assert.Equal(1d / 3, report.SuccessAccuracy!.Value, precision: 12);
        Assert.Equal(0.25, report.FullInputAccuracy, precision: 12);
        Assert.Equal(0.75, report.Coverage, precision: 12);
        Assert.Equal(2, report.ByLanguage["zh"].InputCount);
        Assert.Equal(0d, report.ByLanguage["zh"].FullInputAccuracy);
    }

    /// <summary>驗證逐筆 prediction 保存 sequence length 與 truncation metadata。</summary>
    [Fact]
    public void Prediction_PreservesSequenceMetadata()
    {
        var prediction = new LayaPhase2Prediction(
            "1",
            "food",
            "food",
            "en",
            LayaPhase2Labels.Categories.ToDictionary(label => label, _ => 1d / 11, StringComparer.Ordinal),
            LayaPhase2Labels.Categories,
            1d / 11,
            0d,
            0.2,
            0.1,
            0.2,
            sequenceLength: 512,
            wasTruncated: true);

        Assert.Equal(512, prediction.SequenceLength);
        Assert.True(prediction.WasTruncated);
    }

    /// <summary>驗證 Top-1、Top-2、Top-3 依固定 option order 穩定計算。</summary>
    [Fact]
    public void Calculate_ReportsTopKWithStableRanking()
    {
        var predictions = new[]
        {
            CreatePrediction("1", "food", "food", "en", ["food", "transfer", "other"]),
            CreatePrediction("2", "other", "food", "en", ["food", "other", "transfer"])
        };

        var report = LayaPhase2Metrics.Calculate(predictions, Array.Empty<LayaPhase2Failure>());

        Assert.Equal(0.5, report.TopK[1].SuccessAccuracy!.Value, precision: 12);
        Assert.Equal(1d, report.TopK[2].SuccessAccuracy!.Value, precision: 12);
        Assert.Equal(1d, report.TopK[3].SuccessAccuracy!.Value, precision: 12);
        Assert.Equal("food", report.Predictions["1"].RankedLabels[0]);
    }

    /// <summary>驗證固定 11 類 Macro F1 會納入未預測類別的零分。</summary>
    [Fact]
    public void Calculate_UsesFixedLabelSetForMacroF1AndTransferShare()
    {
        var predictions = new[]
        {
            CreatePrediction("1", "food", "transfer", "en", ["transfer", "food", "other"]),
            CreatePrediction("2", "transfer", "transfer", "en", ["transfer", "food", "other"])
        };

        var report = LayaPhase2Metrics.Calculate(predictions, Array.Empty<LayaPhase2Failure>());

        Assert.Equal(0, report.PerCategory["food"].F1);
        Assert.Equal(2, report.PerCategory["transfer"].PredictedCount);
        Assert.Equal(0.5, report.ExpectedShares["food"], precision: 12);
        Assert.Equal(1d, report.PredictedShares["transfer"], precision: 12);
        Assert.Equal(0.5, report.TransferShareCollapse, precision: 12);
        Assert.Equal(2d / 33, report.MacroF1, precision: 12);
    }

    /// <summary>驗證全失敗 run 保留輸入分母並以 N/A 表示沒有成功分母。</summary>
    [Fact]
    public void Calculate_AllFailuresReturnsNaForSuccessMetrics()
    {
        var report = LayaPhase2Metrics.Calculate(
            Array.Empty<LayaPhase2Prediction>(),
            new[]
            {
                new LayaPhase2Failure("1", "inference", "LayaInferenceException", "en"),
                new LayaPhase2Failure("2", "calibration", "LayaCalibrationException", "zh")
            });

        Assert.Equal(2, report.InputCount);
        Assert.Null(report.SuccessAccuracy);
        Assert.Equal(0d, report.FullInputAccuracy);
        Assert.Equal(0d, report.Coverage);
        Assert.Equal(0d, report.MacroF1);
        Assert.Null(report.TopK[1].SuccessAccuracy);
    }

    /// <summary>驗證缺少固定 label 的非法 probability distribution 會拒絕計算。</summary>
    [Fact]
    public void Calculate_RejectsIncompleteDistribution()
    {
        var prediction = CreatePrediction("1", "food", "food", "en", ["food", "transfer", "other"]);
        var incomplete = new LayaPhase2Prediction(
            prediction.Id,
            prediction.ExpectedLabel,
            prediction.PredictedLabel,
            prediction.Language,
            new Dictionary<string, double> { ["food"] = 1d },
            prediction.RankedLabels,
            prediction.Probability,
            prediction.Margin,
            prediction.NoulProbability,
            null,
            null);

        Assert.Throws<Laya.Core.Exceptions.LayaConfigurationException>(() =>
            LayaPhase2Metrics.Calculate(new[] { incomplete }, Array.Empty<LayaPhase2Failure>()));
    }

    /// <summary>建立具有固定 option order 與完整 probability 的測試 prediction。</summary>
    private static LayaPhase2Prediction CreatePrediction(
        string id,
        string expected,
        string predicted,
        string language,
        IReadOnlyList<string> rankedLabels)
    {
        var probabilities = LayaPhase2Labels.Categories.ToDictionary(
            label => label,
            label => label == rankedLabels[0] ? 0.7 : label == rankedLabels[1] ? 0.2 : 0.1 / 9,
            StringComparer.Ordinal);
        return new LayaPhase2Prediction(
            id,
            expected,
            predicted,
            language,
            probabilities,
            rankedLabels,
            0.7,
            0.5,
            0.2,
            0.1,
            0.2);
    }
}
