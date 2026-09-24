using Laya.Core.Exceptions;

namespace Laya.Core.Evaluation;

/// <summary>保存單一 Noul P(true) bucket 的分類與分組平均。</summary>
public sealed record LayaNoulBucket(
    string Name,
    double LowerBound,
    double UpperBound,
    bool IncludesUpperBound,
    int Count,
    double? Accuracy,
    double? CorrectMean,
    double? WrongMean);

/// <summary>保存五個 Noul bucket 的獨立診斷結果。</summary>
public sealed class LayaNoulReport
{
    /// <summary>建立 Noul report。</summary>
    public LayaNoulReport(IReadOnlyList<LayaNoulBucket> buckets)
    {
        Buckets = buckets;
    }

    /// <summary>取得固定五個 Noul buckets。</summary>
    public IReadOnlyList<LayaNoulBucket> Buckets { get; }
}

/// <summary>分析 needs_review P(true)，不把 Noul 分數套用成 policy override。</summary>
public static class LayaNoulAnalyzer
{
    private static readonly (double Lower, double Upper)[] Ranges =
    {
        (0, 0.2), (0.2, 0.4), (0.4, 0.6), (0.6, 0.8), (0.8, 1)
    };

    /// <summary>計算五個互斥 bucket 的 count、accuracy 與正確／錯誤平均。</summary>
    public static LayaNoulReport Analyze(IEnumerable<LayaPhase2Prediction> predictions)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        var samples = predictions.ToArray();
        return new LayaNoulReport(Ranges.Select((range, index) =>
        {
            var last = index == Ranges.Length - 1;
            var values = samples.Where(sample =>
            {
                ValidateValue(sample.Id, sample.NoulProbability);
                return sample.NoulProbability >= range.Lower &&
                    (last ? sample.NoulProbability <= range.Upper : sample.NoulProbability < range.Upper);
            }).ToArray();
            var correct = values.Where(sample => sample.ExpectedLabel == sample.PredictedLabel).ToArray();
            var wrong = values.Where(sample => sample.ExpectedLabel != sample.PredictedLabel).ToArray();
            return new LayaNoulBucket(
                FormatName(range.Lower, range.Upper, last),
                range.Lower,
                range.Upper,
                last,
                values.Length,
                values.Length == 0 ? null : (double)correct.Length / values.Length,
                correct.Length == 0 ? null : correct.Average(sample => sample.NoulProbability),
                wrong.Length == 0 ? null : wrong.Average(sample => sample.NoulProbability));
        }).ToArray());
    }

    /// <summary>格式化五個 Noul bucket 的固定顯示名稱。</summary>
    private static string FormatName(double lower, double upper, bool last)
    {
        return $"[{lower:0.##},{upper:0.##}{(last ? "]" : ")")}";
    }

    /// <summary>驗證 Noul P(true) 為有限且位於 [0,1]。</summary>
    private static void ValidateValue(string id, double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new LayaConfigurationException(
                $"Prediction '{id}' has Noul P(true) outside [0,1].");
        }
    }
}
