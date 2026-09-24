using Laya.Core.Exceptions;

namespace Laya.Core.Evaluation;

/// <summary>保存 Phase 2 固定的 11 類 category option 順序。</summary>
public static class LayaPhase2Labels
{
    /// <summary>取得不可變更的 baseline category 順序。</summary>
    public static IReadOnlyList<string> Categories { get; } = new[]
    {
        "food", "transport", "shopping", "utilities", "transfer", "salary",
        "bank_fee", "investment", "medical", "entertainment", "other"
    };
}

/// <summary>保存一筆成功的 Phase 2 category prediction 及完整分布。</summary>
public sealed class LayaPhase2Prediction
{
    /// <summary>建立一筆含 stable ranking、Noul 與兩種 latency 的 prediction。</summary>
    public LayaPhase2Prediction(
        string id,
        string expectedLabel,
        string predictedLabel,
        string language,
        IReadOnlyDictionary<string, double> probabilities,
        IReadOnlyList<string> rankedLabels,
        double probability,
        double margin,
        double noulProbability,
        double? runOnlyLatencyMilliseconds,
        double? endToEndLatencyMilliseconds,
        int? sequenceLength = null,
        bool? wasTruncated = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(predictedLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(probabilities);
        ArgumentNullException.ThrowIfNull(rankedLabels);

        Id = id;
        ExpectedLabel = expectedLabel;
        PredictedLabel = predictedLabel;
        Language = language;
        Probabilities = new Dictionary<string, double>(probabilities, StringComparer.Ordinal);
        RankedLabels = rankedLabels.ToArray();
        Probability = probability;
        Margin = margin;
        NoulProbability = noulProbability;
        RunOnlyLatencyMilliseconds = runOnlyLatencyMilliseconds;
        EndToEndLatencyMilliseconds = endToEndLatencyMilliseconds;
        SequenceLength = sequenceLength;
        WasTruncated = wasTruncated;
    }

    /// <summary>取得交易識別碼。</summary>
    public string Id { get; }

    /// <summary>取得人工標註 category。</summary>
    public string ExpectedLabel { get; }

    /// <summary>取得 top-1 category。</summary>
    public string PredictedLabel { get; }

    /// <summary>取得資料語言。</summary>
    public string Language { get; }

    /// <summary>取得完整未四捨五入 category probabilities。</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; }

    /// <summary>取得依固定 option order tie-break 的 ranking。</summary>
    public IReadOnlyList<string> RankedLabels { get; }

    /// <summary>取得 top-1 probability。</summary>
    public double Probability { get; }

    /// <summary>取得 top-1 與 top-2 的 margin。</summary>
    public double Margin { get; }

    /// <summary>取得 needs_review 的 P(true)。</summary>
    public double NoulProbability { get; }

    /// <summary>取得 Run-only latency。</summary>
    public double? RunOnlyLatencyMilliseconds { get; }

    /// <summary>取得 end-to-end latency。</summary>
    public double? EndToEndLatencyMilliseconds { get; }

    /// <summary>取得 request 的實際未 padding sequence length。</summary>
    public int? SequenceLength { get; }

    /// <summary>取得 request 是否達到 model max length 而被截斷。</summary>
    public bool? WasTruncated { get; }

    /// <summary>建立替換 ranking 的 prediction，保留其餘未 rounding 結果。</summary>
    public LayaPhase2Prediction WithRanking(IReadOnlyList<string> rankedLabels)
    {
        return new LayaPhase2Prediction(
            Id,
            ExpectedLabel,
            PredictedLabel,
            Language,
            Probabilities,
            rankedLabels,
            Probability,
            Margin,
            NoulProbability,
            RunOnlyLatencyMilliseconds,
            EndToEndLatencyMilliseconds,
            SequenceLength,
            WasTruncated);
    }
}

/// <summary>保存未成功完成推論的非敏感失敗摘要。</summary>
public sealed record LayaPhase2Failure(
    string Id,
    string Stage,
    string ErrorType,
    string? Language = null);

/// <summary>保存一次 Phase 2 run 的可追溯版本與策略身分。</summary>
public sealed record LayaPhase2RunManifest(
    string RunId,
    string Profile,
    string ModelRevision,
    string DatasetHash,
    string Split,
    string PromptVariant,
    string PromptHash,
    string? PolicyName,
    DateTimeOffset CreatedUtc,
    string Command,
    string? GuidelineHash = null,
    string? DatasetManifestHash = null,
    string? SelectedIdsHash = null,
    string? BundleManifestSha256 = null,
    string? ReferenceManifestSha256 = null,
    string? OptionOrderHash = null,
    string? SerializationVersion = null,
    string? SerializationHash = null,
    string? PolicyHash = null,
    string? FrozenCandidateManifestSha256 = null,
    IReadOnlyList<string>? SelectedIds = null,
    string? FixtureHash = null,
    string? SourceCommit = null,
    bool? SourceDirty = null,
    string? SourceDigest = null,
    string? EnvironmentFingerprint = null,
    int? ThreadCount = null,
    string? FrozenCandidatePath = null);

/// <summary>保存逐筆結果、失敗與 run manifest 的完整輸入集合。</summary>
public sealed class LayaPhase2RunResult
{
    /// <summary>建立一個可由 report writer 重算的 Phase 2 run。</summary>
    public LayaPhase2RunResult(
        int inputCount,
        LayaPhase2RunManifest manifest,
        IReadOnlyList<LayaPhase2Prediction> predictions,
        IReadOnlyList<LayaPhase2Failure> failures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputCount);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(predictions);
        ArgumentNullException.ThrowIfNull(failures);

        if (predictions.Count + failures.Count != inputCount)
        {
            throw new LayaConfigurationException(
                "Phase 2 run inputCount must equal successful predictions plus failures.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prediction in predictions)
        {
            if (!ids.Add(prediction.Id))
            {
                throw new LayaConfigurationException(
                    $"Phase 2 run contains duplicate result id '{prediction.Id}'.");
            }
        }

        foreach (var failure in failures)
        {
            if (!ids.Add(failure.Id))
            {
                throw new LayaConfigurationException(
                    $"Phase 2 run contains duplicate result id '{failure.Id}'.");
            }
        }

        InputCount = inputCount;
        Manifest = manifest;
        Predictions = predictions.ToArray();
        Failures = failures.ToArray();
    }

    /// <summary>取得輸入總數，包含失敗列。</summary>
    public int InputCount { get; }

    /// <summary>取得 run manifest。</summary>
    public LayaPhase2RunManifest Manifest { get; }

    /// <summary>取得成功 prediction。</summary>
    public IReadOnlyList<LayaPhase2Prediction> Predictions { get; }

    /// <summary>取得失敗摘要。</summary>
    public IReadOnlyList<LayaPhase2Failure> Failures { get; }

    /// <summary>取得是否所有輸入都成功完成。</summary>
    public bool Complete => Failures.Count == 0;
}

/// <summary>保存單一 category 的 Phase 2 support、precision、recall 與 F1。</summary>
public sealed record LayaPhase2CategoryMetrics(
    int Support,
    int PredictedCount,
    int TruePositive,
    double? Precision,
    double? Recall,
    double F1);

/// <summary>保存單一語言的成功與全輸入分母。</summary>
public sealed record LayaPhase2LanguageMetrics(
    int InputCount,
    int SuccessCount,
    int CorrectCount,
    double? SuccessAccuracy,
    double? FullInputAccuracy);

/// <summary>保存單一 Top-k 的成功與全輸入 accuracy。</summary>
public sealed record LayaPhase2TopKMetrics(
    int CorrectCount,
    double? SuccessAccuracy,
    double FullInputAccuracy);

/// <summary>保存 Phase 2 品質、分布、語言與失敗分母 metrics。</summary>
public sealed class LayaPhase2MetricsReport
{
    /// <summary>建立完整 Phase 2 metrics snapshot。</summary>
    public LayaPhase2MetricsReport(
        int inputCount,
        int successCount,
        int failureCount,
        int correctCount,
        double? successAccuracy,
        double fullInputAccuracy,
        IReadOnlyDictionary<int, LayaPhase2TopKMetrics> topK,
        IReadOnlyDictionary<string, LayaPhase2CategoryMetrics> perCategory,
        double macroF1,
        IReadOnlyDictionary<string, LayaPhase2LanguageMetrics> byLanguage,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> confusionMatrix,
        IReadOnlyDictionary<string, double> expectedShares,
        IReadOnlyDictionary<string, double> predictedShares,
        double transferShareCollapse,
        IReadOnlyDictionary<string, LayaPhase2Prediction> predictions,
        double coverage)
    {
        InputCount = inputCount;
        SuccessCount = successCount;
        FailureCount = failureCount;
        CorrectCount = correctCount;
        SuccessAccuracy = successAccuracy;
        FullInputAccuracy = fullInputAccuracy;
        TopK = topK;
        PerCategory = perCategory;
        MacroF1 = macroF1;
        ByLanguage = byLanguage;
        ConfusionMatrix = confusionMatrix;
        ExpectedShares = expectedShares;
        PredictedShares = predictedShares;
        TransferShareCollapse = transferShareCollapse;
        Predictions = predictions;
        Coverage = coverage;
    }

    /// <summary>取得全輸入數。</summary>
    public int InputCount { get; }

    /// <summary>取得成功數。</summary>
    public int SuccessCount { get; }

    /// <summary>取得失敗數。</summary>
    public int FailureCount { get; }

    /// <summary>取得 top-1 正確數。</summary>
    public int CorrectCount { get; }

    /// <summary>取得成功樣本 accuracy；無成功樣本時為 N/A。</summary>
    public double? SuccessAccuracy { get; }

    /// <summary>取得以全輸入為分母的 accuracy。</summary>
    public double FullInputAccuracy { get; }

    /// <summary>取得 Top-1、Top-2、Top-3 metrics。</summary>
    public IReadOnlyDictionary<int, LayaPhase2TopKMetrics> TopK { get; }

    /// <summary>取得固定 labels 的 per-category metrics。</summary>
    public IReadOnlyDictionary<string, LayaPhase2CategoryMetrics> PerCategory { get; }

    /// <summary>取得固定 11 類 Macro F1。</summary>
    public double MacroF1 { get; }

    /// <summary>取得各語言 metrics。</summary>
    public IReadOnlyDictionary<string, LayaPhase2LanguageMetrics> ByLanguage { get; }

    /// <summary>取得成功樣本 confusion matrix。</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ConfusionMatrix { get; }

    /// <summary>取得成功樣本的 expected category share。</summary>
    public IReadOnlyDictionary<string, double> ExpectedShares { get; }

    /// <summary>取得成功樣本的 predicted category share。</summary>
    public IReadOnlyDictionary<string, double> PredictedShares { get; }

    /// <summary>取得 transfer predicted share 減 expected share 的 collapse 指標。</summary>
    public double TransferShareCollapse { get; }

    /// <summary>取得 normalized prediction，供 report 從原始結果重算。</summary>
    public IReadOnlyDictionary<string, LayaPhase2Prediction> Predictions { get; }

    /// <summary>取得成功數除以全輸入數的 coverage。</summary>
    public double Coverage { get; }
}

/// <summary>計算 Phase 2 固定分母與固定 label set 的分類 metrics。</summary>
public static class LayaPhase2Metrics
{
    /// <summary>計算品質、Top-k、語言、confusion、share 與 failure metrics。</summary>
    public static LayaPhase2MetricsReport Calculate(
        IEnumerable<LayaPhase2Prediction> predictions,
        IEnumerable<LayaPhase2Failure> failures,
        IReadOnlyList<string>? labels = null)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        ArgumentNullException.ThrowIfNull(failures);
        var configuredLabels = labels ?? LayaPhase2Labels.Categories;
        var labelSet = CreateLabelSet(configuredLabels);
        var normalized = NormalizePredictions(predictions, configuredLabels, labelSet);
        var failureList = failures.ToArray();
        ValidateFailureIds(normalized, failureList);
        var inputCount = normalized.Count + failureList.Length;
        if (inputCount == 0)
        {
            throw new LayaConfigurationException("At least one Phase 2 input is required.");
        }

        var correctCount = normalized.Count(item => item.ExpectedLabel == item.PredictedLabel);
        var perCategory = configuredLabels.ToDictionary(
            label => label,
            label => CreateCategoryMetrics(normalized, label),
            StringComparer.Ordinal);
        var macroF1 = perCategory.Values.Average(item => item.F1);
        var topK = CreateTopKMetrics(normalized, failureList.Length, inputCount);
        var byLanguage = CreateLanguageMetrics(normalized, failureList);
        var confusion = CreateConfusionMatrix(normalized, configuredLabels);
        var expectedShares = CreateShares(normalized, configuredLabels, sample => sample.ExpectedLabel);
        var predictedShares = CreateShares(normalized, configuredLabels, sample => sample.PredictedLabel);
        var transferCollapse = predictedShares.GetValueOrDefault("transfer") -
            expectedShares.GetValueOrDefault("transfer");

        return new LayaPhase2MetricsReport(
            inputCount,
            normalized.Count,
            failureList.Length,
            correctCount,
            normalized.Count == 0 ? null : (double)correctCount / normalized.Count,
            (double)correctCount / inputCount,
            topK,
            perCategory,
            macroF1,
            byLanguage,
            confusion,
            expectedShares,
            predictedShares,
            transferCollapse,
            normalized.ToDictionary(item => item.Id, StringComparer.Ordinal),
            (double)normalized.Count / inputCount);
    }

    /// <summary>建立並驗證不重複的固定 label set。</summary>
    private static HashSet<string> CreateLabelSet(IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
        {
            throw new LayaConfigurationException("Phase 2 labels cannot be empty.");
        }

        var labelSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label) || !labelSet.Add(label))
            {
                throw new LayaConfigurationException("Phase 2 labels must be non-empty and unique.");
            }
        }

        return labelSet;
    }

    /// <summary>驗證 prediction 並補齊 stable ranking 後回傳 immutable list。</summary>
    private static IReadOnlyList<LayaPhase2Prediction> NormalizePredictions(
        IEnumerable<LayaPhase2Prediction> predictions,
        IReadOnlyList<string> labels,
        HashSet<string> labelSet)
    {
        var result = new List<LayaPhase2Prediction>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prediction in predictions)
        {
            if (!ids.Add(prediction.Id))
            {
                throw new LayaConfigurationException(
                    $"Phase 2 predictions contain duplicate id '{prediction.Id}'.");
            }

            ValidatePrediction(prediction, labels, labelSet);
            var ranking = prediction.RankedLabels.Count == 0
                ? labels.OrderByDescending(label => prediction.Probabilities[label])
                    .ThenBy(label => Array.IndexOf(labels.ToArray(), label))
                    .ToArray()
                : NormalizeRanking(prediction, labels, labelSet);
            result.Add(prediction.WithRanking(ranking));
        }

        return result;
    }

    /// <summary>驗證單一 prediction 的 labels、probability、margin 與 latency。</summary>
    private static void ValidatePrediction(
        LayaPhase2Prediction prediction,
        IReadOnlyList<string> labels,
        HashSet<string> labelSet)
    {
        if (!labelSet.Contains(prediction.ExpectedLabel) || !labelSet.Contains(prediction.PredictedLabel))
        {
            throw new LayaConfigurationException(
                $"Prediction '{prediction.Id}' contains a label outside the configured labels.");
        }

        if (prediction.Probabilities.Count != labels.Count ||
            labels.Any(label => !prediction.Probabilities.ContainsKey(label)) ||
            prediction.Probabilities.Values.Any(value => !double.IsFinite(value) || value < 0 || value > 1))
        {
            throw new LayaConfigurationException(
                $"Prediction '{prediction.Id}' must contain finite probabilities for every label.");
        }

        if (!IsProbability(prediction.Probability) || !IsProbability(prediction.Margin) ||
            !IsProbability(prediction.NoulProbability) ||
            !IsOptionalLatency(prediction.RunOnlyLatencyMilliseconds) ||
            !IsOptionalLatency(prediction.EndToEndLatencyMilliseconds))
        {
            throw new LayaConfigurationException(
                $"Prediction '{prediction.Id}' contains an invalid probability, margin or latency.");
        }
    }

    /// <summary>依固定 option order 驗證並補齊傳入 ranking。</summary>
    private static IReadOnlyList<string> NormalizeRanking(
        LayaPhase2Prediction prediction,
        IReadOnlyList<string> labels,
        HashSet<string> labelSet)
    {
        if (prediction.RankedLabels.Count > labels.Count ||
            prediction.RankedLabels.Any(label => !labelSet.Contains(label)) ||
            prediction.RankedLabels.Distinct(StringComparer.Ordinal).Count() != prediction.RankedLabels.Count)
        {
            throw new LayaConfigurationException(
                $"Prediction '{prediction.Id}' contains an invalid ranking.");
        }

        var missing = labels
            .Where(label => !prediction.RankedLabels.Contains(label, StringComparer.Ordinal))
            .OrderByDescending(label => prediction.Probabilities[label])
            .ThenBy(label => Array.IndexOf(labels.ToArray(), label))
            .ToArray();
        return prediction.RankedLabels.Concat(missing).ToArray();
    }

    /// <summary>驗證失敗與成功結果的 ID 不重複。</summary>
    private static void ValidateFailureIds(
        IReadOnlyList<LayaPhase2Prediction> predictions,
        IReadOnlyList<LayaPhase2Failure> failures)
    {
        var ids = predictions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var failure in failures)
        {
            if (!ids.Add(failure.Id))
            {
                throw new LayaConfigurationException(
                    $"Phase 2 failures contain duplicate or successful id '{failure.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(failure.Stage) || string.IsNullOrWhiteSpace(failure.ErrorType))
            {
                throw new LayaConfigurationException(
                    $"Phase 2 failure '{failure.Id}' must contain stage and error type.");
            }
        }
    }

    /// <summary>計算固定 labels 的 precision、recall 與 F1。</summary>
    private static LayaPhase2CategoryMetrics CreateCategoryMetrics(
        IReadOnlyList<LayaPhase2Prediction> predictions,
        string label)
    {
        var support = predictions.Count(item => item.ExpectedLabel == label);
        var predicted = predictions.Count(item => item.PredictedLabel == label);
        var truePositive = predictions.Count(item =>
            item.ExpectedLabel == label && item.PredictedLabel == label);
        var falsePositive = predicted - truePositive;
        var falseNegative = support - truePositive;
        var denominator = (2 * truePositive) + falsePositive + falseNegative;
        return new LayaPhase2CategoryMetrics(
            support,
            predicted,
            truePositive,
            predicted == 0 ? null : (double)truePositive / predicted,
            support == 0 ? null : (double)truePositive / support,
            denominator == 0 ? 0 : (double)(2 * truePositive) / denominator);
    }

    /// <summary>計算 Top-1、Top-2、Top-3 的成功與全輸入 accuracy。</summary>
    private static IReadOnlyDictionary<int, LayaPhase2TopKMetrics> CreateTopKMetrics(
        IReadOnlyList<LayaPhase2Prediction> predictions,
        int failureCount,
        int inputCount)
    {
        return new[] { 1, 2, 3 }.ToDictionary(
            k => k,
            k =>
            {
                var correct = predictions.Count(item =>
                    item.RankedLabels.Take(k).Contains(item.ExpectedLabel, StringComparer.Ordinal));
                return new LayaPhase2TopKMetrics(
                    correct,
                    predictions.Count == 0 ? null : (double)correct / predictions.Count,
                    (double)correct / inputCount);
            });
    }

    /// <summary>計算各語言的成功與全輸入分母，未知語言失敗不捏造歸屬。</summary>
    private static IReadOnlyDictionary<string, LayaPhase2LanguageMetrics> CreateLanguageMetrics(
        IReadOnlyList<LayaPhase2Prediction> predictions,
        IReadOnlyList<LayaPhase2Failure> failures)
    {
        var languages = predictions.Select(item => item.Language)
            .Concat(failures.Where(item => item.Language is not null).Select(item => item.Language!))
            .Distinct(StringComparer.Ordinal);
        return languages.ToDictionary(
            language => language,
            language =>
            {
                var successful = predictions.Where(item => item.Language == language).ToArray();
                var failed = failures.Count(item => item.Language == language);
                var correct = successful.Count(item => item.ExpectedLabel == item.PredictedLabel);
                var inputCount = successful.Length + failed;
                return new LayaPhase2LanguageMetrics(
                    inputCount,
                    successful.Length,
                    correct,
                    successful.Length == 0 ? null : (double)correct / successful.Length,
                    inputCount == 0 ? null : (double)correct / inputCount);
            },
            StringComparer.Ordinal);
    }

    /// <summary>建立只包含成功 prediction 的完整 confusion matrix。</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CreateConfusionMatrix(
        IReadOnlyList<LayaPhase2Prediction> predictions,
        IReadOnlyList<string> labels)
    {
        var matrix = labels.ToDictionary(
            expected => expected,
            _ => labels.ToDictionary(predicted => predicted, _ => 0, StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var prediction in predictions)
        {
            matrix[prediction.ExpectedLabel][prediction.PredictedLabel]++;
        }

        return matrix.ToDictionary(
            item => item.Key,
            item => (IReadOnlyDictionary<string, int>)item.Value,
            StringComparer.Ordinal);
    }

    /// <summary>計算成功樣本的 expected 或 predicted category share。</summary>
    private static IReadOnlyDictionary<string, double> CreateShares(
        IReadOnlyList<LayaPhase2Prediction> predictions,
        IReadOnlyList<string> labels,
        Func<LayaPhase2Prediction, string> selector)
    {
        return labels.ToDictionary(
            label => label,
            label => predictions.Count == 0
                ? 0
                : (double)predictions.Count(item => selector(item) == label) / predictions.Count,
            StringComparer.Ordinal);
    }

    /// <summary>驗證數值是否為 [0,1] 內的有限 probability。</summary>
    private static bool IsProbability(double value)
    {
        return double.IsFinite(value) && value >= 0 && value <= 1;
    }

    /// <summary>驗證可選 latency 為非負有限值。</summary>
    private static bool IsOptionalLatency(double? value)
    {
        return value is null || (double.IsFinite(value.Value) && value.Value >= 0);
    }
}
