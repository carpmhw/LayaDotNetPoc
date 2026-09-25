namespace Laya.MemoryBenchmarks.Analysis;

/// <summary>保存單一 replicate 的 PrivateMemory／RSS peak、end 與 late slopes。</summary>
internal sealed class MemoryReplicateMeasurement
{
    /// <summary>建立帶有獨立 run ID 的 memory replicate metrics。</summary>
    public MemoryReplicateMeasurement(
        string runId,
        long peakPrivateMemoryBytes,
        long endPrivateMemoryBytes,
        double latePrivateMemorySlopeBytesPer100Requests,
        long peakRssBytes,
        long endRssBytes,
        double lateRssSlopeBytesPer100Requests)
    {
        RunId = runId;
        PeakPrivateMemoryBytes = peakPrivateMemoryBytes;
        EndPrivateMemoryBytes = endPrivateMemoryBytes;
        LatePrivateMemorySlopeBytesPer100Requests = latePrivateMemorySlopeBytesPer100Requests;
        PeakRssBytes = peakRssBytes;
        EndRssBytes = endRssBytes;
        LateRssSlopeBytesPer100Requests = lateRssSlopeBytesPer100Requests;
    }

    /// <summary>取得唯一 run ID。</summary>
    public string RunId { get; }

    /// <summary>取得 PrivateMemory peak bytes。</summary>
    public long PeakPrivateMemoryBytes { get; }

    /// <summary>取得 PrivateMemory end bytes。</summary>
    public long EndPrivateMemoryBytes { get; }

    /// <summary>取得 PrivateMemory 2500-5000 segment slope。</summary>
    public double LatePrivateMemorySlopeBytesPer100Requests { get; }

    /// <summary>取得 RSS peak bytes。</summary>
    public long PeakRssBytes { get; }

    /// <summary>取得 RSS end bytes。</summary>
    public long EndRssBytes { get; }

    /// <summary>取得 RSS 2500-5000 segment slope。</summary>
    public double LateRssSlopeBytesPer100Requests { get; }
}

/// <summary>保存 replicate pair tolerance assessment 與第三次執行決策。</summary>
internal sealed class MemoryReplicateAssessment
{
    /// <summary>建立 replicate consistency assessment。</summary>
    public MemoryReplicateAssessment(bool isConsistent, bool requiresThirdRun, string? reason)
    {
        IsConsistent = isConsistent;
        RequiresThirdRun = requiresThirdRun;
        Reason = reason;
    }

    /// <summary>取得是否所有 replicate pair 均在 frozen policy tolerances 內。</summary>
    public bool IsConsistent { get; }

    /// <summary>取得是否在現有兩個 replicate 後需要排程第三次。</summary>
    public bool RequiresThirdRun { get; }

    /// <summary>取得 inconsistency 或不足 replicate 原因。</summary>
    public string? Reason { get; }
}

/// <summary>以 frozen policy peak/end/slope tolerances 判斷重現性並決定是否補第三次。</summary>
internal static class MemoryReplicateAnalyzer
{
    /// <summary>比較獨立 run metrics；只在恰有兩次且超出 tolerance 時要求第三次。</summary>
    public static MemoryReplicateAssessment Assess(
        IReadOnlyList<MemoryReplicateMeasurement> measurements,
        MemoryReplicateTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(tolerance);
        if (measurements.Select(item => item.RunId).Distinct(StringComparer.Ordinal).Count() != measurements.Count)
        {
            throw new ArgumentException("Replicate measurements must have unique run IDs.", nameof(measurements));
        }

        if (measurements.Count < 2)
        {
            return new MemoryReplicateAssessment(
                isConsistent: false,
                requiresThirdRun: true,
                "At least two independent runs are required before reproducibility can be assessed.");
        }

        for (var leftIndex = 0; leftIndex < measurements.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < measurements.Count; rightIndex++)
            {
                if (!WithinTolerance(measurements[leftIndex], measurements[rightIndex], tolerance))
                {
                    return new MemoryReplicateAssessment(
                        isConsistent: false,
                        requiresThirdRun: measurements.Count == 2,
                        measurements.Count == 2
                            ? "The two independent runs exceed frozen tolerance; run a third replicate."
                            : "The third replicate did not resolve the frozen-tolerance disagreement.");
                }
            }
        }

        return new MemoryReplicateAssessment(true, false, null);
    }

    /// <summary>確認六個 PrivateMemory/RSS peak、end 與 slope metrics 均符合 tolerance。</summary>
    private static bool WithinTolerance(
        MemoryReplicateMeasurement left,
        MemoryReplicateMeasurement right,
        MemoryReplicateTolerance tolerance)
    {
        return RelativeDifference(left.PeakPrivateMemoryBytes, right.PeakPrivateMemoryBytes) <= tolerance.PeakRelativeDifferenceMaximum &&
            RelativeDifference(left.PeakRssBytes, right.PeakRssBytes) <= tolerance.PeakRelativeDifferenceMaximum &&
            RelativeDifference(left.EndPrivateMemoryBytes, right.EndPrivateMemoryBytes) <= tolerance.EndRelativeDifferenceMaximum &&
            RelativeDifference(left.EndRssBytes, right.EndRssBytes) <= tolerance.EndRelativeDifferenceMaximum &&
            RelativeDifference(left.LatePrivateMemorySlopeBytesPer100Requests, right.LatePrivateMemorySlopeBytesPer100Requests) <= tolerance.SlopeRelativeDifferenceMaximum &&
            RelativeDifference(left.LateRssSlopeBytesPer100Requests, right.LateRssSlopeBytesPer100Requests) <= tolerance.SlopeRelativeDifferenceMaximum;
    }

    /// <summary>計算以兩值絕對值較大者為 denominator 的 relative difference。</summary>
    private static double RelativeDifference(double left, double right)
    {
        var leftValue = Math.Abs(left);
        var rightValue = Math.Abs(right);
        var denominator = Math.Max(leftValue, rightValue);
        return denominator == 0 ? 0 : Math.Abs(left - right) / denominator;
    }
}
