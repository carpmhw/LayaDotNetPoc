extern alias MemoryProbe;

using MemoryProbe::Laya.MemoryProbe.Telemetry;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryProbeDiagnosticsTests
{
    /// <summary>驗證 idle wait 依指定秒數順序等待並於每段後擷取 sample。</summary>
    [Fact]
    public async Task ObserveIdleAsync_WaitsAndSamplesInConfiguredOrder()
    {
        var waits = new List<int>();
        var samples = new List<int>();

        await MemoryProbeDiagnostics.ObserveIdleAsync(
            new[] { 30, 60, 300 },
            (duration, _) =>
            {
                waits.Add(checked((int)duration.TotalSeconds));
                return ValueTask.CompletedTask;
            },
            samples.Add);

        Assert.Equal(new[] { 30, 60, 300 }, waits);
        Assert.Equal(new[] { 30, 60, 300 }, samples);
    }

    /// <summary>驗證 forced GC 診斷以 GC 前後 checkpoint 包住完整收集序列。</summary>
    [Fact]
    public void ForceFullGc_CapturesBeforeAndAfterCollection()
    {
        var checkpoints = new List<string>();
        var generation2Before = GC.CollectionCount(2);

        MemoryProbeDiagnostics.ForceFullGc(checkpoints.Add);

        Assert.Equal(new[] { "forced-gc-before", "forced-gc-after" }, checkpoints);
        Assert.True(GC.CollectionCount(2) > generation2Before);
    }

    /// <summary>驗證 idle wait 拒絕負數秒數且不呼叫 delay delegate。</summary>
    [Fact]
    public async Task ObserveIdleAsync_RejectsNegativeDuration()
    {
        var waitCalled = false;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await MemoryProbeDiagnostics.ObserveIdleAsync(
                new[] { 0, -1 },
                (_, _) =>
                {
                    waitCalled = true;
                    return ValueTask.CompletedTask;
                },
                _ => { }));

        Assert.False(waitCalled);
    }
}
