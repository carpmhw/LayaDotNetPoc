extern alias MemoryProbe;

using MemoryProbe::Laya.MemoryProbe.Telemetry;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class BoundedLatencyWindowTests
{
    /// <summary>驗證 latency summary 使用 `(n-1)*p` 線性插值並保存最後一筆。</summary>
    [Fact]
    public void SnapshotAndReset_UsesInterpolatedPercentiles()
    {
        var window = new BoundedLatencyWindow(capacity: 4);
        window.Add(TimeSpan.FromMilliseconds(1));
        window.Add(TimeSpan.FromMilliseconds(2));
        window.Add(TimeSpan.FromMilliseconds(3));
        window.Add(TimeSpan.FromMilliseconds(4));

        var summary = window.SnapshotAndReset();

        Assert.NotNull(summary);
        Assert.Equal(4, summary.SampleCount);
        Assert.Equal(2.5, summary.MeanMilliseconds, precision: 6);
        Assert.Equal(2.5, summary.P50Milliseconds, precision: 6);
        Assert.Equal(3.85, summary.P95Milliseconds, precision: 6);
        Assert.Equal(3.97, summary.P99Milliseconds, precision: 6);
        Assert.Equal(4, summary.LastMilliseconds);
        Assert.Null(window.SnapshotAndReset());
    }

    /// <summary>驗證 latency window 不會超過建立時配置的固定容量。</summary>
    [Fact]
    public void Add_RejectsSamplesPastConfiguredCapacity()
    {
        var window = new BoundedLatencyWindow(capacity: 1);
        window.Add(TimeSpan.FromMilliseconds(1));

        Assert.Throws<InvalidOperationException>(() =>
        {
            window.Add(TimeSpan.FromMilliseconds(2));
        });
    }

    /// <summary>驗證負值及非有限 latency 不會進入統計窗口。</summary>
    [Theory]
    [InlineData(-1)]
    public void Add_RejectsInvalidLatency(double milliseconds)
    {
        var window = new BoundedLatencyWindow(capacity: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            window.Add(TimeSpan.FromMilliseconds(milliseconds));
        });
    }
}
