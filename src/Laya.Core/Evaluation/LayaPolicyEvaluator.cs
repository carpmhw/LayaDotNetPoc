using Laya.Core.Exceptions;

namespace Laya.Core.Evaluation;

/// <summary>表示 Phase 2 三種探索性 policy scenario。</summary>
public enum LayaPolicyScenario
{
    /// <summary>以 98% POC accuracy target 搜尋最大 coverage。</summary>
    Conservative,

    /// <summary>以 95% POC accuracy target 搜尋最大 coverage。</summary>
    Balanced,

    /// <summary>不設 accuracy target，僅作不安全探索性參考。</summary>
    Aggressive
}

/// <summary>保存顯式 Phase 2 AUTO／SUGGEST／REVIEW 門檻。</summary>
public sealed record LayaPhase2Policy(
    string Name,
    bool AutoEnabled,
    double AutoProbabilityThreshold,
    double AutoMarginThreshold,
    double SuggestProbabilityThreshold,
    double SuggestMarginThreshold);

/// <summary>保存一組 probability AND margin threshold 的完整指標。</summary>
public sealed record LayaPolicyGridCell(
    double ProbabilityThreshold,
    double MarginThreshold,
    int QualifiedCount,
    double? Accuracy,
    double Coverage,
    int WrongCount);

/// <summary>保存完整 72 組 grid 與全輸入分母。</summary>
public sealed class LayaPolicyGridReport
{
    /// <summary>建立 threshold grid report。</summary>
    public LayaPolicyGridReport(int inputCount, IReadOnlyList<LayaPolicyGridCell> cells)
    {
        InputCount = inputCount;
        Cells = cells;
    }

    /// <summary>取得全輸入數。</summary>
    public int InputCount { get; }

    /// <summary>取得 9x8 的所有 cells。</summary>
    public IReadOnlyList<LayaPolicyGridCell> Cells { get; }
}

/// <summary>保存 scenario candidate、AUTO 狀態與停用原因。</summary>
public sealed class LayaPolicyScenarioResult
{
    /// <summary>建立 scenario 搜尋結果。</summary>
    public LayaPolicyScenarioResult(
        LayaPolicyScenario scenario,
        double? targetAccuracy,
        bool autoEnabled,
        LayaPolicyGridCell? selectedCell,
        string reason)
    {
        Scenario = scenario;
        TargetAccuracy = targetAccuracy;
        AutoEnabled = autoEnabled;
        SelectedCell = selectedCell;
        Reason = reason;
    }

    /// <summary>取得 scenario 名稱。</summary>
    public LayaPolicyScenario Scenario { get; }

    /// <summary>取得 POC target；Aggressive 為 null。</summary>
    public double? TargetAccuracy { get; }

    /// <summary>取得是否允許該 candidate 的 AUTO。</summary>
    public bool AutoEnabled { get; }

    /// <summary>取得選定 grid cell；無達標組時為 null。</summary>
    public LayaPolicyGridCell? SelectedCell { get; }

    /// <summary>取得選擇或停用理由。</summary>
    public string Reason { get; }
}

/// <summary>執行 Phase 2 threshold grid、scenario 搜尋與顯式 policy 分類。</summary>
public static class LayaPolicyEvaluator
{
    /// <summary>固定 probability grid，總共九個門檻。</summary>
    public static IReadOnlyList<double> ProbabilityThresholds { get; } =
        new[] { 0.60, 0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 0.97, 0.99 };

    /// <summary>固定 margin grid，總共八個門檻。</summary>
    public static IReadOnlyList<double> MarginThresholds { get; } =
        new[] { 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.70, 0.80 };

    /// <summary>計算所有 72 組 AND threshold，coverage 分母固定為全輸入。</summary>
    public static LayaPolicyGridReport EvaluateGrid(
        IEnumerable<LayaPhase2Prediction> predictions,
        int inputCount)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        if (inputCount <= 0)
        {
            throw new LayaConfigurationException("Policy grid inputCount must be greater than zero.");
        }

        var samples = predictions.ToArray();
        if (samples.Length > inputCount)
        {
            throw new LayaConfigurationException(
                "Policy grid successful predictions cannot exceed inputCount.");
        }

        foreach (var sample in samples)
        {
            ValidateScore(sample.Id, sample.Probability, "probability");
            ValidateScore(sample.Id, sample.Margin, "margin");
        }

        var cells = (
            from probabilityThreshold in ProbabilityThresholds
            from marginThreshold in MarginThresholds
            let qualified = samples.Where(sample =>
                sample.Probability >= probabilityThreshold && sample.Margin >= marginThreshold).ToArray()
            let correct = qualified.Count(sample => sample.ExpectedLabel == sample.PredictedLabel)
            select new LayaPolicyGridCell(
                probabilityThreshold,
                marginThreshold,
                qualified.Length,
                qualified.Length == 0 ? null : (double)correct / qualified.Length,
                (double)qualified.Length / inputCount,
                qualified.Length - correct)).ToArray();

        return new LayaPolicyGridReport(inputCount, cells);
    }

    /// <summary>依 target、coverage、wrong count、probability、margin 做 deterministic tie-break。</summary>
    public static LayaPolicyScenarioResult SelectScenario(
        LayaPolicyGridReport grid,
        LayaPolicyScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var target = scenario switch
        {
            LayaPolicyScenario.Conservative => 0.98,
            LayaPolicyScenario.Balanced => 0.95,
            LayaPolicyScenario.Aggressive => (double?)null,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var candidates = grid.Cells
            .Where(cell => cell.QualifiedCount > 0 &&
                (target is null || cell.Accuracy >= target.Value))
            .OrderByDescending(cell => cell.Coverage)
            .ThenBy(cell => cell.WrongCount)
            .ThenByDescending(cell => cell.ProbabilityThreshold)
            .ThenByDescending(cell => cell.MarginThreshold)
            .ToArray();
        if (candidates.Length == 0)
        {
            var reason = target is null
                ? "No non-empty grid cell exists; AUTO is disabled."
                : $"No non-empty grid cell reached the POC target {target.Value:P0}; AUTO is disabled.";
            return new LayaPolicyScenarioResult(scenario, target, false, null, reason);
        }

        return new LayaPolicyScenarioResult(
            scenario,
            target,
            true,
            candidates[0],
            "Selected by maximum coverage, then fewer wrong AUTO, higher probability threshold and higher margin threshold.");
    }

    /// <summary>將 development scenario 固定成顯式 policy，SUGGEST 初始值固定為 .60/.10。</summary>
    public static LayaPhase2Policy CreatePolicy(
        LayaPolicyScenarioResult scenario,
        double suggestProbabilityThreshold = 0.60,
        double suggestMarginThreshold = 0.10)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var selected = scenario.SelectedCell;
        return new LayaPhase2Policy(
            scenario.Scenario.ToString(),
            scenario.AutoEnabled,
            selected?.ProbabilityThreshold ?? 1,
            selected?.MarginThreshold ?? 1,
            suggestProbabilityThreshold,
            suggestMarginThreshold);
    }

    /// <summary>依顯式 AUTO→SUGGEST→REVIEW 順序分類成功 prediction。</summary>
    public static LayaDecisionMode Classify(
        LayaPhase2Prediction prediction,
        LayaPhase2Policy policy)
    {
        ArgumentNullException.ThrowIfNull(prediction);
        ArgumentNullException.ThrowIfNull(policy);
        ValidatePolicy(policy);
        ValidateScore(prediction.Id, prediction.Probability, "probability");
        ValidateScore(prediction.Id, prediction.Margin, "margin");

        if (policy.AutoEnabled &&
            prediction.Probability >= policy.AutoProbabilityThreshold &&
            prediction.Margin >= policy.AutoMarginThreshold)
        {
            return LayaDecisionMode.Auto;
        }

        return prediction.Probability >= policy.SuggestProbabilityThreshold &&
            prediction.Margin >= policy.SuggestMarginThreshold
            ? LayaDecisionMode.Suggest
            : LayaDecisionMode.Review;
    }

    /// <summary>將任何推論失敗固定分類為 REVIEW，不因 policy 門檻而遺失分母。</summary>
    public static LayaDecisionMode ClassifyFailure(LayaPhase2Policy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ValidatePolicy(policy);
        return LayaDecisionMode.Review;
    }

    /// <summary>回傳 policy 的可重算身分，供 candidate 與 run provenance 比對。</summary>
    public static string GetPolicyHash(LayaPhase2Policy policy)
    {
        return LayaPhase2Contracts.PolicyHash(policy);
    }

    /// <summary>驗證 policy 所有門檻為有限且位於 [0,1]。</summary>
    private static void ValidatePolicy(LayaPhase2Policy policy)
    {
        ValidateThreshold(policy.AutoProbabilityThreshold, nameof(policy.AutoProbabilityThreshold));
        ValidateThreshold(policy.AutoMarginThreshold, nameof(policy.AutoMarginThreshold));
        ValidateThreshold(policy.SuggestProbabilityThreshold, nameof(policy.SuggestProbabilityThreshold));
        ValidateThreshold(policy.SuggestMarginThreshold, nameof(policy.SuggestMarginThreshold));
    }

    /// <summary>驗證單一 prediction score 位於 [0,1]。</summary>
    private static void ValidateScore(string id, double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new LayaConfigurationException(
                $"Prediction '{id}' has {name} outside [0,1].");
        }
    }

    /// <summary>驗證 policy threshold 位於 [0,1]。</summary>
    private static void ValidateThreshold(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new LayaConfigurationException(
                $"Policy threshold '{name}' must be finite and in [0,1].");
        }
    }
}
