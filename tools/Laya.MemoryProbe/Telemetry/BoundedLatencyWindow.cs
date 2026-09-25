namespace Laya.MemoryProbe.Telemetry;

/// <summary>以固定容量保存單一 checkpoint window 的 latency 數值。</summary>
internal sealed class BoundedLatencyWindow
{
    private readonly double[] _samples;
    private int _count;
    private double _lastMilliseconds;

    /// <summary>建立固定容量 latency window，避免 measured requests 引發無界增長。</summary>
    public BoundedLatencyWindow(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _samples = new double[capacity];
    }

    /// <summary>取得 window 已收集的 sample 數。</summary>
    public int Count => _count;

    /// <summary>加入非負 latency；超過配置容量時立即失敗。</summary>
    public void Add(TimeSpan latency)
    {
        var milliseconds = latency.TotalMilliseconds;
        if (milliseconds < 0 || !double.IsFinite(milliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(latency), "Latency must be finite and non-negative.");
        }

        if (_count >= _samples.Length)
        {
            throw new InvalidOperationException("Latency window capacity was exceeded before checkpoint reset.");
        }

        _samples[_count++] = milliseconds;
        _lastMilliseconds = milliseconds;
    }

    /// <summary>計算統計摘要、清空已使用槽位並重用固定陣列。</summary>
    public MemoryLatencySummary? SnapshotAndReset()
    {
        if (_count == 0)
        {
            return null;
        }

        var sorted = new double[_count];
        Array.Copy(_samples, sorted, _count);
        Array.Sort(sorted);
        var mean = 0d;
        foreach (var value in sorted)
        {
            mean += value;
        }

        var summary = new MemoryLatencySummary(
            _count,
            mean / _count,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[0],
            sorted[^1],
            _lastMilliseconds);
        Array.Clear(_samples, 0, _count);
        _count = 0;
        _lastMilliseconds = 0;
        return summary;
    }

    /// <summary>依排序樣本與 `(n-1)*p` 線性插值計算 percentile。</summary>
    private static double Percentile(IReadOnlyList<double> sorted, double probability)
    {
        var position = (sorted.Count - 1) * probability;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }
}
