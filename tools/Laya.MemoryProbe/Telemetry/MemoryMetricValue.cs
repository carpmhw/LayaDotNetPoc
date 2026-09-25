namespace Laya.MemoryProbe.Telemetry;

/// <summary>保存以 bytes 表示的可選 memory metric 與不可用原因。</summary>
internal sealed class MemoryMetricValue
{
    /// <summary>建立可用或不可用 memory metric。</summary>
    public MemoryMetricValue(long? bytes, string? unavailableReason)
    {
        Bytes = bytes;
        UnavailableReason = unavailableReason;
    }

    /// <summary>取得 memory metric bytes；不可用時為 null。</summary>
    public long? Bytes { get; }

    /// <summary>取得不可用原因；有值時 metric 不可用。</summary>
    public string? UnavailableReason { get; }

    /// <summary>建立可用 bytes metric。</summary>
    public static MemoryMetricValue Available(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return new MemoryMetricValue(bytes, null);
    }

    /// <summary>將 signed counter 轉成有效 bytes 或帶有負值原因的 unavailable metric。</summary>
    public static MemoryMetricValue FromReportedBytes(long bytes, string metricName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName);
        return bytes >= 0
            ? Available(bytes)
            : Unavailable($"{metricName} reported a negative byte count ({bytes}).");
    }

    /// <summary>建立帶有診斷原因的不可用 metric。</summary>
    public static MemoryMetricValue Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new MemoryMetricValue(null, reason);
    }
}
