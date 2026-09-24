namespace Laya.Core.Parity;

/// <summary>保存 parity probability 比較的絕對誤差結果。</summary>
public sealed record LayaParityComparisonResult(
    double Expected,
    double Actual,
    double AbsoluteError,
    bool IsWithinTolerance,
    double Tolerance);

/// <summary>集中管理 reference parity tolerance 與 provenance 規則。</summary>
public static class LayaParityTolerance
{
    /// <summary>預設 probability absolute tolerance。</summary>
    public const double Default = 1e-4;

    /// <summary>任何明確調查都不可超過的最大 tolerance。</summary>
    public const double Maximum = 5e-4;

    /// <summary>比較單一 probability，答案相同不會掩蓋數值超標。</summary>
    public static LayaParityComparisonResult CompareProbability(
        double expected,
        double actual,
        double tolerance = Default,
        string? provenance = null)
    {
        Validate(tolerance, provenance);
        if (!double.IsFinite(expected) || !double.IsFinite(actual))
        {
            throw new ArgumentException("Parity probabilities must be finite.");
        }

        var absoluteError = Math.Abs(expected - actual);
        return new LayaParityComparisonResult(
            expected,
            actual,
            absoluteError,
            absoluteError <= tolerance,
            tolerance);
    }

    /// <summary>驗證 tolerance 上限與任何放寬設定的調查 provenance。</summary>
    public static void Validate(double tolerance, string? provenance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0 || tolerance > Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tolerance),
                $"Parity tolerance must be finite and no greater than {Maximum:R}.");
        }

        if (tolerance > Default && string.IsNullOrWhiteSpace(provenance))
        {
            throw new ArgumentException(
                "A relaxed parity tolerance requires an investigation provenance.",
                nameof(provenance));
        }
    }
}
