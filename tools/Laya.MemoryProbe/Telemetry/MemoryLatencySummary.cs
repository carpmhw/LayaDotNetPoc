namespace Laya.MemoryProbe.Telemetry;

/// <summary>保存單一 latency window 的樣本數、平均、percentiles 與極值。</summary>
internal sealed class MemoryLatencySummary
{
    /// <summary>建立 bounded latency window summary。</summary>
    public MemoryLatencySummary(
        int sampleCount,
        double meanMilliseconds,
        double p50Milliseconds,
        double p95Milliseconds,
        double p99Milliseconds,
        double minMilliseconds,
        double maxMilliseconds,
        double lastMilliseconds)
    {
        SampleCount = sampleCount;
        MeanMilliseconds = meanMilliseconds;
        P50Milliseconds = p50Milliseconds;
        P95Milliseconds = p95Milliseconds;
        P99Milliseconds = p99Milliseconds;
        MinMilliseconds = minMilliseconds;
        MaxMilliseconds = maxMilliseconds;
        LastMilliseconds = lastMilliseconds;
    }

    /// <summary>取得 window 內 measured request 數。</summary>
    public int SampleCount { get; }

    /// <summary>取得平均 end-to-end latency 毫秒。</summary>
    public double MeanMilliseconds { get; }

    /// <summary>取得 P50 end-to-end latency 毫秒。</summary>
    public double P50Milliseconds { get; }

    /// <summary>取得 P95 end-to-end latency 毫秒。</summary>
    public double P95Milliseconds { get; }

    /// <summary>取得 P99 end-to-end latency 毫秒。</summary>
    public double P99Milliseconds { get; }

    /// <summary>取得 window 最小 latency 毫秒。</summary>
    public double MinMilliseconds { get; }

    /// <summary>取得 window 最大 latency 毫秒。</summary>
    public double MaxMilliseconds { get; }

    /// <summary>取得 window 最後一筆 latency 毫秒。</summary>
    public double LastMilliseconds { get; }
}
