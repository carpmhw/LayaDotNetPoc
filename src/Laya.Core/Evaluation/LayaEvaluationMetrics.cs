using Laya.Core.Exceptions;

namespace Laya.Core.Evaluation;

/// <summary>代表一筆已完成 prediction 的交易評估樣本。</summary>
public sealed class LayaEvaluationSample
{
    /// <summary>建立交易評估樣本。</summary>
    public LayaEvaluationSample(
        string id,
        string expectedLabel,
        string predictedLabel,
        double probability,
        double margin,
        string language,
        double? latencyMilliseconds = null)
    {
        Id = id;
        ExpectedLabel = expectedLabel;
        PredictedLabel = predictedLabel;
        Probability = probability;
        Margin = margin;
        Language = language;
        LatencyMilliseconds = latencyMilliseconds;
    }

    /// <summary>取得樣本識別碼。</summary>
    public string Id { get; }

    /// <summary>取得人工標註的 category。</summary>
    public string ExpectedLabel { get; }

    /// <summary>取得模型選出的 category。</summary>
    public string PredictedLabel { get; }

    /// <summary>取得模型對 top-1 category 的 probability。</summary>
    public double Probability { get; }

    /// <summary>取得 top-1 與 top-2 probability 的 margin。</summary>
    public double Margin { get; }

    /// <summary>取得資料語言標籤。</summary>
    public string Language { get; }

    /// <summary>取得單筆 Run-only latency；無記錄時為 null。</summary>
    public double? LatencyMilliseconds { get; }
}

/// <summary>保存單一 category 的分類指標。</summary>
public sealed class LayaClassMetrics
{
    /// <summary>建立單一 category 指標。</summary>
    public LayaClassMetrics(
        int support,
        int predictedCount,
        int truePositive,
        double? accuracy,
        double? precision,
        double? recall,
        double? f1)
    {
        Support = support;
        PredictedCount = predictedCount;
        TruePositive = truePositive;
        Accuracy = accuracy;
        Precision = precision;
        Recall = recall;
        F1 = f1;
    }

    /// <summary>取得實際屬於該 category 的樣本數。</summary>
    public int Support { get; }

    /// <summary>取得模型預測為該 category 的樣本數。</summary>
    public int PredictedCount { get; }

    /// <summary>取得 true positive 數量。</summary>
    public int TruePositive { get; }

    /// <summary>取得該 category 的 accuracy，也就是正確數除以 support。</summary>
    public double? Accuracy { get; }

    /// <summary>取得該 category 的 precision。</summary>
    public double? Precision { get; }

    /// <summary>取得該 category 的 recall。</summary>
    public double? Recall { get; }

    /// <summary>取得該 category 的 F1。</summary>
    public double? F1 { get; }
}

/// <summary>保存單一 confidence threshold 的 coverage 與 accuracy。</summary>
public sealed class LayaThresholdMetrics
{
    /// <summary>建立 threshold 指標。</summary>
    public LayaThresholdMetrics(double coverage, double? accuracy, int includedCount)
    {
        Coverage = coverage;
        Accuracy = accuracy;
        IncludedCount = includedCount;
    }

    /// <summary>取得達到 threshold 的樣本比例。</summary>
    public double Coverage { get; }

    /// <summary>取得達到 threshold 的樣本 accuracy。</summary>
    public double? Accuracy { get; }

    /// <summary>取得達到 threshold 的樣本數。</summary>
    public int IncludedCount { get; }
}

/// <summary>保存單一語言分組的分類結果。</summary>
public sealed class LayaLanguageMetrics
{
    /// <summary>建立語言分組指標。</summary>
    public LayaLanguageMetrics(int sampleCount, int correctCount, double accuracy)
    {
        SampleCount = sampleCount;
        CorrectCount = correctCount;
        Accuracy = accuracy;
    }

    /// <summary>取得該語言的樣本數。</summary>
    public int SampleCount { get; }

    /// <summary>取得該語言預測正確的樣本數。</summary>
    public int CorrectCount { get; }

    /// <summary>取得該語言的 accuracy。</summary>
    public double Accuracy { get; }
}

/// <summary>保存整批交易的 classification report。</summary>
public sealed class LayaEvaluationReport
{
    /// <summary>建立 classification report。</summary>
    public LayaEvaluationReport(
        int sampleCount,
        int correctCount,
        double overallAccuracy,
        IReadOnlyDictionary<string, LayaClassMetrics> perClass,
        IReadOnlyDictionary<string, LayaLanguageMetrics> byLanguage,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> confusionMatrix,
        IReadOnlyDictionary<double, LayaThresholdMetrics> thresholds)
    {
        SampleCount = sampleCount;
        CorrectCount = correctCount;
        OverallAccuracy = overallAccuracy;
        PerClass = perClass;
        ByLanguage = byLanguage;
        ConfusionMatrix = confusionMatrix;
        Thresholds = thresholds;
    }

    /// <summary>取得評估樣本總數。</summary>
    public int SampleCount { get; }

    /// <summary>取得預測正確的樣本數。</summary>
    public int CorrectCount { get; }

    /// <summary>取得整體 accuracy。</summary>
    public double OverallAccuracy { get; }

    /// <summary>取得各 category 指標。</summary>
    public IReadOnlyDictionary<string, LayaClassMetrics> PerClass { get; }

    /// <summary>取得依 CSV language 欄位分組的指標。</summary>
    public IReadOnlyDictionary<string, LayaLanguageMetrics> ByLanguage { get; }

    /// <summary>取得 expected 到 predicted 的 confusion matrix。</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ConfusionMatrix { get; }

    /// <summary>取得各 confidence threshold 指標。</summary>
    public IReadOnlyDictionary<double, LayaThresholdMetrics> Thresholds { get; }
}

/// <summary>計算 transaction CSV 所需的分類與 confidence 指標。</summary>
public static class LayaEvaluationMetrics
{
    private static readonly double[] DefaultThresholds = { 0.60, 0.70, 0.80, 0.85, 0.90, 0.95 };

    /// <summary>依指定 labels 計算整體、各類別、confusion matrix 與 threshold coverage。</summary>
    public static LayaEvaluationReport Calculate(
        IEnumerable<LayaEvaluationSample> samples,
        IReadOnlyList<string> labels,
        IReadOnlyList<double>? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(labels);

        var labelSet = CreateLabelSet(labels);
        var materialized = samples.ToArray();
        if (materialized.Length == 0)
        {
            throw new LayaConfigurationException("At least one evaluation sample is required.");
        }

        foreach (var sample in materialized)
        {
            ValidateSample(sample, labelSet);
        }

        var correctCount = materialized.Count(sample =>
            string.Equals(sample.ExpectedLabel, sample.PredictedLabel, StringComparison.Ordinal));
        var confusion = CreateConfusionMatrix(labels);
        foreach (var sample in materialized)
        {
            confusion[sample.ExpectedLabel][sample.PredictedLabel]++;
        }

        var perClass = labels.ToDictionary(
            label => label,
            label => CreateClassMetrics(materialized, label),
            StringComparer.Ordinal);
        var byLanguage = CreateLanguageMetrics(materialized);
        var thresholdMetrics = CreateThresholdMetrics(materialized, thresholds ?? DefaultThresholds);

        return new LayaEvaluationReport(
            materialized.Length,
            correctCount,
            (double)correctCount / materialized.Length,
            perClass,
            byLanguage,
            confusion.ToDictionary(
                item => item.Key,
                item => (IReadOnlyDictionary<string, int>)item.Value,
                StringComparer.Ordinal),
            thresholdMetrics);
    }

    /// <summary>建立且驗證不重複的 label set。</summary>
    private static HashSet<string> CreateLabelSet(IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
        {
            throw new LayaConfigurationException("At least one evaluation label is required.");
        }

        var labelSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label) || !labelSet.Add(label))
            {
                throw new LayaConfigurationException("Evaluation labels must be non-empty and unique.");
            }
        }

        return labelSet;
    }

    /// <summary>驗證樣本 label、probability 與 margin 的範圍。</summary>
    private static void ValidateSample(LayaEvaluationSample sample, HashSet<string> labelSet)
    {
        if (!labelSet.Contains(sample.ExpectedLabel) || !labelSet.Contains(sample.PredictedLabel))
        {
            throw new LayaConfigurationException(
                $"Evaluation sample '{sample.Id}' contains a label outside the configured labels.");
        }

        if (!double.IsFinite(sample.Probability) || sample.Probability < 0 || sample.Probability > 1 ||
            !double.IsFinite(sample.Margin) || sample.Margin < 0 || sample.Margin > 1)
        {
            throw new LayaConfigurationException(
                $"Evaluation sample '{sample.Id}' has probability or margin outside [0,1].");
        }

        if (sample.LatencyMilliseconds is not null &&
            (!double.IsFinite(sample.LatencyMilliseconds.Value) || sample.LatencyMilliseconds.Value < 0))
        {
            throw new LayaConfigurationException(
                $"Evaluation sample '{sample.Id}' has an invalid latency value.");
        }
    }

    /// <summary>建立空白 confusion matrix。</summary>
    private static Dictionary<string, Dictionary<string, int>> CreateConfusionMatrix(
        IReadOnlyList<string> labels)
    {
        return labels.ToDictionary(
            expected => expected,
            _ => labels.ToDictionary(predicted => predicted, _ => 0, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    /// <summary>計算單一 category 的 support、precision、recall 與 F1。</summary>
    private static LayaClassMetrics CreateClassMetrics(
        IReadOnlyList<LayaEvaluationSample> samples,
        string label)
    {
        var support = samples.Count(sample => sample.ExpectedLabel == label);
        var predictedCount = samples.Count(sample => sample.PredictedLabel == label);
        var truePositive = samples.Count(sample =>
            sample.ExpectedLabel == label && sample.PredictedLabel == label);
        double? precision = predictedCount == 0 ? null : (double)truePositive / predictedCount;
        double? recall = support == 0 ? null : (double)truePositive / support;
        double? f1 = precision is null || recall is null
            ? null
            : precision.Value + recall.Value == 0
                ? 0d
                : 2 * precision.Value * recall.Value / (precision.Value + recall.Value);

        return new LayaClassMetrics(support, predictedCount, truePositive, recall, precision, recall, f1);
    }

    /// <summary>依樣本 Language 欄位計算各語言的 sample count 與 accuracy。</summary>
    private static Dictionary<string, LayaLanguageMetrics> CreateLanguageMetrics(
        IReadOnlyList<LayaEvaluationSample> samples)
    {
        return samples
            .GroupBy(sample => sample.Language, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var values = group.ToArray();
                    var correct = values.Count(sample => sample.ExpectedLabel == sample.PredictedLabel);
                    return new LayaLanguageMetrics(values.Length, correct, (double)correct / values.Length);
                },
                StringComparer.Ordinal);
    }

    /// <summary>計算 confidence threshold 的 coverage 與條件 accuracy。</summary>
    private static Dictionary<double, LayaThresholdMetrics> CreateThresholdMetrics(
        IReadOnlyList<LayaEvaluationSample> samples,
        IReadOnlyList<double> thresholds)
    {
        var metrics = new Dictionary<double, LayaThresholdMetrics>();
        foreach (var threshold in thresholds)
        {
            if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1)
            {
                throw new LayaConfigurationException("Confidence thresholds must be finite values in [0,1].");
            }

            var included = samples.Where(sample => sample.Probability >= threshold).ToArray();
            var correct = included.Count(sample => sample.ExpectedLabel == sample.PredictedLabel);
            metrics[threshold] = new LayaThresholdMetrics(
                (double)included.Length / samples.Count,
                included.Length == 0 ? null : (double)correct / included.Length,
                included.Length);
        }

        return metrics;
    }
}
