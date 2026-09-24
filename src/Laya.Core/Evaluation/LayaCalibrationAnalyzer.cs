using Laya.Core.Exceptions;

namespace Laya.Core.Evaluation;

/// <summary>保存一個 confidence、margin 或 ECE bucket 的原始統計。</summary>
public sealed record LayaCalibrationBucket(
    string Name,
    double LowerBound,
    double UpperBound,
    bool IncludesUpperBound,
    int Count,
    double? Accuracy,
    double? MeanValue,
    double Contribution);

/// <summary>保存 Phase 2 confidence、margin 與十等寬 ECE 分析。</summary>
public sealed class LayaCalibrationReport
{
    /// <summary>建立 calibration report。</summary>
    public LayaCalibrationReport(
        int successCount,
        IReadOnlyList<LayaCalibrationBucket> confidenceBuckets,
        IReadOnlyList<LayaCalibrationBucket> eceBins,
        IReadOnlyList<LayaCalibrationBucket> marginBuckets,
        double? ece)
    {
        SuccessCount = successCount;
        ConfidenceBuckets = confidenceBuckets;
        EceBins = eceBins;
        MarginBuckets = marginBuckets;
        Ece = ece;
    }

    /// <summary>取得成功樣本數。</summary>
    public int SuccessCount { get; }

    /// <summary>取得指定 confidence buckets。</summary>
    public IReadOnlyList<LayaCalibrationBucket> ConfidenceBuckets { get; }

    /// <summary>取得十等寬 ECE bins。</summary>
    public IReadOnlyList<LayaCalibrationBucket> EceBins { get; }

    /// <summary>取得指定 margin buckets。</summary>
    public IReadOnlyList<LayaCalibrationBucket> MarginBuckets { get; }

    /// <summary>取得 ECE；無成功樣本時為 N/A。</summary>
    public double? Ece { get; }
}

/// <summary>以固定邊界分析成功 prediction 的 calibration，不修改模型輸出。</summary>
public static class LayaCalibrationAnalyzer
{
    private static readonly (double Lower, double Upper)[] ConfidenceRanges =
    {
        (0, 0.5), (0.5, 0.6), (0.6, 0.7), (0.7, 0.8),
        (0.8, 0.9), (0.9, 0.95), (0.95, 0.99), (0.99, 1)
    };

    private static readonly (double Lower, double Upper)[] MarginRanges =
    {
        (0, 0.1), (0.1, 0.2), (0.2, 0.4), (0.4, 0.6),
        (0.6, 0.8), (0.8, 1)
    };

    /// <summary>計算 confidence buckets、margin buckets 與十等寬 ECE。</summary>
    public static LayaCalibrationReport Analyze(IEnumerable<LayaPhase2Prediction> predictions)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        var samples = predictions.ToArray();
        foreach (var sample in samples)
        {
            ValidateValue(sample.Id, sample.Probability, "probability");
            ValidateValue(sample.Id, sample.Margin, "margin");
        }

        var confidence = CreateBuckets(
            samples,
            ConfidenceRanges,
            sample => sample.Probability,
            "confidence");
        var margin = CreateBuckets(
            samples,
            MarginRanges,
            sample => sample.Margin,
            "margin");
        var eceBins = CreateBuckets(
            samples,
            CreateEqualRanges(10),
            sample => sample.Probability,
            "ece");
        double? ece = samples.Length == 0
            ? null
            : eceBins.Sum(bucket => bucket.Contribution);

        return new LayaCalibrationReport(samples.Length, confidence, eceBins, margin, ece);
    }

    /// <summary>建立一般分桶並計算 count、accuracy、mean value。</summary>
    private static IReadOnlyList<LayaCalibrationBucket> CreateBuckets(
        IReadOnlyList<LayaPhase2Prediction> samples,
        IReadOnlyList<(double Lower, double Upper)> ranges,
        Func<LayaPhase2Prediction, double> selector,
        string prefix)
    {
        return ranges.Select((range, index) =>
        {
            var isLast = index == ranges.Count - 1;
            var values = samples
                .Where(sample => IsInRange(selector(sample), range.Lower, range.Upper, isLast))
                .ToArray();
            double? accuracy = values.Length == 0
                ? null
                : values.Count(sample => sample.ExpectedLabel == sample.PredictedLabel) / (double)values.Length;
            double? meanValue = values.Length == 0 ? null : values.Average(selector);
            var contribution = prefix == "ece" && values.Length > 0
                ? values.Length / (double)samples.Count * Math.Abs(accuracy!.Value - meanValue!.Value)
                : 0d;
            return new LayaCalibrationBucket(
                FormatName(prefix, range.Lower, range.Upper, isLast),
                range.Lower,
                range.Upper,
                isLast,
                values.Length,
                accuracy,
                meanValue,
                contribution);
        }).ToArray();
    }

    /// <summary>建立指定數量的 [0,1] 等寬範圍。</summary>
    private static IReadOnlyList<(double Lower, double Upper)> CreateEqualRanges(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => (index / (double)count, (index + 1) / (double)count))
            .ToArray();
    }

    /// <summary>套用左閉右開且最後包含 1 的 bucket 邊界規則。</summary>
    private static bool IsInRange(double value, double lower, double upper, bool isLast)
    {
        return value >= lower && (isLast ? value <= upper : value < upper);
    }

    /// <summary>格式化報告中可讀且可追溯的 bucket 名稱。</summary>
    private static string FormatName(string prefix, double lower, double upper, bool isLast)
    {
        var close = isLast ? "]" : ")";
        return $"{prefix}[{lower:0.##},{upper:0.##}{close}";
    }

    /// <summary>驗證 calibration input 為有限且位於 [0,1]。</summary>
    private static void ValidateValue(string id, double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new LayaConfigurationException(
                $"Prediction '{id}' has {name} outside [0,1].");
        }
    }
}
