namespace Laya.MemoryBenchmarks.Analysis;

/// <summary>保存單一 metric sample 的 request 軸、bytes 值與自然／GC 身分。</summary>
internal sealed class MemorySamplePoint
{
    /// <summary>建立一個 process／managed memory observation。</summary>
    public MemorySamplePoint(int requestCount, long? bytes, int sampleIndex, bool isForcedGc = false)
    {
        RequestCount = requestCount;
        Bytes = bytes;
        SampleIndex = sampleIndex;
        IsForcedGc = isForcedGc;
    }

    /// <summary>取得 warmup 後成功完成的 request count。</summary>
    public int RequestCount { get; }

    /// <summary>取得 bytes 值；counter unavailable 時為 null。</summary>
    public long? Bytes { get; }

    /// <summary>取得 run 內 sample index，用於同 request count 穩定選擇最後 observation。</summary>
    public int SampleIndex { get; }

    /// <summary>取得 sample 是否緊接 forced GC diagnostic。</summary>
    public bool IsForcedGc { get; }
}

/// <summary>保存一個完整或資料不足的 segmented slope 結果。</summary>
internal sealed class MemorySlopeSegmentResult
{
    /// <summary>建立一個可重算的 slope segment result。</summary>
    public MemorySlopeSegmentResult(
        int startRequest,
        int endRequest,
        int sampleCount,
        double? slopeBytesPer100Requests,
        long? endpointDeltaBytes,
        double? rSquared,
        string? unavailableReason)
    {
        StartRequest = startRequest;
        EndRequest = endRequest;
        SampleCount = sampleCount;
        SlopeBytesPer100Requests = slopeBytesPer100Requests;
        EndpointDeltaBytes = endpointDeltaBytes;
        RSquared = rSquared;
        UnavailableReason = unavailableReason;
    }

    /// <summary>取得 segment 起始 request boundary。</summary>
    public int StartRequest { get; }

    /// <summary>取得 segment 結束 request boundary。</summary>
    public int EndRequest { get; }

    /// <summary>取得納入擬合的非-forced-GC observation count。</summary>
    public int SampleCount { get; }

    /// <summary>取得 OLS slope bytes／100 requests；資料不足時為 null。</summary>
    public double? SlopeBytesPer100Requests { get; }

    /// <summary>取得 exact endpoint delta bytes；endpoint 不足時為 null。</summary>
    public long? EndpointDeltaBytes { get; }

    /// <summary>取得 OLS coefficient of determination；資料不足時為 null。</summary>
    public double? RSquared { get; }

    /// <summary>取得資料不足或 metric unavailable 原因。</summary>
    public string? UnavailableReason { get; }

    /// <summary>取得兩個 exact endpoint 與至少兩個 x observations 是否齊備。</summary>
    public bool IsComplete => UnavailableReason is null;
}

/// <summary>分析自然 memory samples 的分段 OLS slopes 與 plateau inputs。</summary>
internal static class MemorySlopeAnalyzer
{
    /// <summary>計算指定 request interval 的 OLS slope，排除 forced-GC samples。</summary>
    public static MemorySlopeSegmentResult AnalyzeSegment(
        IReadOnlyList<MemorySamplePoint> points,
        int startRequest,
        int endRequest)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (startRequest < 0 || endRequest <= startRequest)
        {
            throw new ArgumentOutOfRangeException(nameof(startRequest), "Slope interval must have increasing non-negative request boundaries.");
        }

        var interval = points
            .Where(point => !point.IsForcedGc && point.RequestCount >= startRequest && point.RequestCount <= endRequest)
            .GroupBy(point => point.RequestCount)
            .Select(group => group.OrderBy(point => point.SampleIndex).Last())
            .OrderBy(point => point.RequestCount)
            .ToArray();
        if (interval.Any(point => point.Bytes is null))
        {
            return Incomplete(startRequest, endRequest, interval.Length, "A required memory metric is unavailable in the segment.");
        }

        var start = interval.FirstOrDefault(point => point.RequestCount == startRequest);
        var end = interval.FirstOrDefault(point => point.RequestCount == endRequest);
        if (start is null || end is null)
        {
            return Incomplete(startRequest, endRequest, interval.Length, "An exact start or end request endpoint is missing.");
        }

        if (interval.Length < 2)
        {
            return Incomplete(startRequest, endRequest, interval.Length, "At least two natural memory samples are required.");
        }

        var meanX = interval.Average(point => (double)point.RequestCount);
        var meanY = interval.Average(point => (double)point.Bytes!.Value);
        var sumXX = 0d;
        var sumXY = 0d;
        var totalSquares = 0d;
        foreach (var point in interval)
        {
            var deltaX = point.RequestCount - meanX;
            var deltaY = point.Bytes!.Value - meanY;
            sumXX += deltaX * deltaX;
            sumXY += deltaX * deltaY;
            totalSquares += deltaY * deltaY;
        }

        if (sumXX == 0)
        {
            return Incomplete(startRequest, endRequest, interval.Length, "Request counts do not provide distinct OLS x values.");
        }

        var slope = sumXY / sumXX;
        var intercept = meanY - slope * meanX;
        var residualSquares = 0d;
        foreach (var point in interval)
        {
            var residual = point.Bytes!.Value - (intercept + slope * point.RequestCount);
            residualSquares += residual * residual;
        }

        var rSquared = totalSquares == 0
            ? residualSquares == 0 ? 1d : 0d
            : 1d - residualSquares / totalSquares;
        return new MemorySlopeSegmentResult(
            startRequest,
            endRequest,
            interval.Length,
            slope * 100d,
            end.Bytes!.Value - start.Bytes!.Value,
            Math.Clamp(rSquared, 0d, 1d),
            null);
    }

    /// <summary>計算 0–500、500–1000、1000–2500 與 2500–5000 固定區段。</summary>
    public static IReadOnlyDictionary<string, MemorySlopeSegmentResult> AnalyzeRequiredSegments(
        IReadOnlyList<MemorySamplePoint> points)
    {
        return new Dictionary<string, MemorySlopeSegmentResult>(StringComparer.Ordinal)
        {
            ["0-500"] = AnalyzeSegment(points, 0, 500),
            ["500-1000"] = AnalyzeSegment(points, 500, 1000),
            ["1000-2500"] = AnalyzeSegment(points, 1000, 2500),
            ["2500-5000"] = AnalyzeSegment(points, 2500, 5000)
        };
    }

    /// <summary>建立 unavailable slope result，不以零值取代缺少的 raw evidence。</summary>
    private static MemorySlopeSegmentResult Incomplete(
        int startRequest,
        int endRequest,
        int sampleCount,
        string reason)
    {
        return new MemorySlopeSegmentResult(startRequest, endRequest, sampleCount, null, null, null, reason);
    }
}
