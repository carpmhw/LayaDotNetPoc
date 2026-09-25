extern alias MemoryProbe;

using System.Collections.Concurrent;
using MemoryProbe::Laya.MemoryProbe.Scenarios;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class BoundedProbeCoordinatorTests
{
    /// <summary>驗證 coordinator 限制同時執行數並對每個 request 僅派送一次。</summary>
    [Fact]
    public async Task ExecuteAsync_UsesBoundedWorkersAndDispatchesEachRequestOnce()
    {
        var coordinator = new BoundedProbeCoordinator();
        var active = 0;
        var maximumActive = 0;
        var indexes = new ConcurrentBag<int>();
        var checkpoints = new ConcurrentBag<int>();

        var result = await coordinator.ExecuteAsync(
            requestCount: 7,
            concurrency: 2,
            sampleEvery: 3,
            async (index, _, _) =>
            {
                var current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximumActive, current);
                indexes.Add(index);
                await Task.Delay(5);
                Interlocked.Decrement(ref active);
                return new ProbeOperationSample(TimeSpan.FromMilliseconds(index + 1), null, false);
            },
            (_, _) => { },
            checkpoints.Add);

        Assert.Equal(7, result.AttemptedRequests);
        Assert.Equal(7, result.CompletedRequests);
        Assert.Equal(0, result.Errors);
        Assert.InRange(maximumActive, 1, 2);
        Assert.Equal(7, indexes.Distinct().Count());
        Assert.Equal(new[] { 3, 6, 7 }, checkpoints.OrderBy(value => value));
    }

    /// <summary>驗證 sample checkpoint 僅在前一批 request 全部 drain 後呼叫。</summary>
    [Fact]
    public async Task ExecuteAsync_WaitsForCurrentBatchBeforeCheckpoint()
    {
        var coordinator = new BoundedProbeCoordinator();
        var completed = 0;
        var checkpoints = new List<(int Count, int Completed)>();

        var result = await coordinator.ExecuteAsync(
            requestCount: 5,
            concurrency: 3,
            sampleEvery: 2,
            async (_, _, _) =>
            {
                await Task.Delay(2);
                Interlocked.Increment(ref completed);
                return new ProbeOperationSample(TimeSpan.FromMilliseconds(1), null, false);
            },
            (_, _) => { },
            count => checkpoints.Add((count, Volatile.Read(ref completed))));

        Assert.Equal(5, result.CompletedRequests);
        Assert.Equal(new[] { (2, 2), (4, 4), (5, 5) }, checkpoints);
    }

    /// <summary>驗證週期 checkpoint 與 10／100／500／1000／2500／5000 必要邊界均會採樣。</summary>
    [Fact]
    public async Task ExecuteAsync_EmitsRequiredMemoryCheckpointsAlongsideSamplingInterval()
    {
        var coordinator = new BoundedProbeCoordinator();
        var checkpoints = new List<int>();

        await coordinator.ExecuteAsync(
            requestCount: 2500,
            concurrency: 4,
            sampleEvery: 1000,
            (index, _, _) => ValueTask.FromResult(
                new ProbeOperationSample(TimeSpan.FromTicks(index + 1), null, false)),
            (_, _) => { },
            checkpoints.Add);

        Assert.Equal(new[] { 10, 100, 500, 1000, 2000, 2500 }, checkpoints);
    }

    /// <summary>驗證 operation exception 與 integrity failure 分開計數並繼續後續工作。</summary>
    [Fact]
    public async Task ExecuteAsync_RecordsErrorsAndIntegrityFailuresSeparately()
    {
        var coordinator = new BoundedProbeCoordinator();

        var result = await coordinator.ExecuteAsync(
            requestCount: 5,
            concurrency: 2,
            sampleEvery: 5,
            (index, _, _) =>
            {
                if (index == 2)
                {
                    throw new InvalidOperationException("synthetic operation failure");
                }

                return ValueTask.FromResult(new ProbeOperationSample(
                    TimeSpan.FromMilliseconds(1),
                    null,
                    integrityFailure: index == 4));
            },
            (_, _) => { },
            _ => { });

        Assert.Equal(5, result.AttemptedRequests);
        Assert.Equal(4, result.CompletedRequests);
        Assert.Equal(1, result.Errors);
        Assert.Equal(1, result.IntegrityFailures);
    }

    /// <summary>驗證取消時停止派工並等待所有已啟動 worker 結束。</summary>
    [Fact]
    public async Task ExecuteAsync_CancellationDrainsInFlightWorkers()
    {
        var coordinator = new BoundedProbeCoordinator();
        using var cancellation = new CancellationTokenSource();
        var releaseWorkers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedWorkers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;

        var execution = coordinator.ExecuteAsync(
            requestCount: 10,
            concurrency: 2,
            sampleEvery: 5,
            async (_, _, _) =>
            {
                if (Interlocked.Increment(ref active) == 2)
                {
                    startedWorkers.TrySetResult();
                }

                await releaseWorkers.Task;
                Interlocked.Decrement(ref active);
                return new ProbeOperationSample(TimeSpan.FromMilliseconds(1), null, false);
            },
            (_, _) => { },
            _ => { },
            cancellation.Token);

        await startedWorkers.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        releaseWorkers.SetResult();
        var result = await execution;

        Assert.True(result.WasCancelled);
        Assert.Equal(0, Volatile.Read(ref active));
        Assert.InRange(result.AttemptedRequests, 1, 2);
    }

    /// <summary>驗證每個固定 worker slot 在同一時間最多執行一個 operation。</summary>
    [Fact]
    public async Task ExecuteAsync_ProvidesExclusiveStableWorkerSlots()
    {
        var coordinator = new BoundedProbeCoordinator();
        var activePerWorker = new int[3];
        var maximumPerWorker = new int[3];

        var result = await coordinator.ExecuteAsync(
            requestCount: 12,
            concurrency: 3,
            sampleEvery: 6,
            async (_, workerIndex, _) =>
            {
                var active = Interlocked.Increment(ref activePerWorker[workerIndex]);
                UpdateMaximum(ref maximumPerWorker[workerIndex], active);
                await Task.Delay(2);
                Interlocked.Decrement(ref activePerWorker[workerIndex]);
                return new ProbeOperationSample(TimeSpan.FromMilliseconds(1), null, false);
            },
            (_, _) => { },
            _ => { });

        Assert.Equal(12, result.CompletedRequests);
        Assert.Equal(new[] { 1, 1, 1 }, maximumPerWorker);
    }

    /// <summary>以 interlocked compare-and-swap 更新最大同時 worker 計數。</summary>
    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var current = Volatile.Read(ref maximum);
        while (candidate > current)
        {
            var previous = Interlocked.CompareExchange(ref maximum, candidate, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}
