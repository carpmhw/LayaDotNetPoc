namespace Laya.MemoryProbe.Scenarios;

/// <summary>保存單一完成 request 的延遲與 integrity 結果。</summary>
internal sealed class ProbeOperationSample
{
    /// <summary>建立一筆已完成 probe operation 的量測值。</summary>
    public ProbeOperationSample(
        TimeSpan endToEndDuration,
        TimeSpan? inferenceDuration,
        bool integrityFailure,
        int? sequenceLength = null,
        bool? wasTruncated = null)
    {
        EndToEndDuration = endToEndDuration;
        InferenceDuration = inferenceDuration;
        IntegrityFailure = integrityFailure;
        SequenceLength = sequenceLength;
        WasTruncated = wasTruncated;
    }

    /// <summary>取得包含 request work 的 end-to-end latency。</summary>
    public TimeSpan EndToEndDuration { get; }

    /// <summary>取得可選的 native inference latency。</summary>
    public TimeSpan? InferenceDuration { get; }

    /// <summary>取得此 request 是否未通過 reference integrity 比對。</summary>
    public bool IntegrityFailure { get; }

    /// <summary>取得此 request 實際 sequence length。</summary>
    public int? SequenceLength { get; }

    /// <summary>取得此 request 是否被 truncated。</summary>
    public bool? WasTruncated { get; }
}

/// <summary>保存 bounded coordinator 的實際派送與完成計數。</summary>
internal sealed class ProbeCoordinatorResult
{
    /// <summary>建立一份 probe coordinator 執行摘要。</summary>
    public ProbeCoordinatorResult(
        int attemptedRequests,
        int completedRequests,
        int errors,
        int integrityFailures,
        bool wasCancelled)
    {
        AttemptedRequests = attemptedRequests;
        CompletedRequests = completedRequests;
        Errors = errors;
        IntegrityFailures = integrityFailures;
        WasCancelled = wasCancelled;
    }

    /// <summary>取得已開始執行的 request 數。</summary>
    public int AttemptedRequests { get; }

    /// <summary>取得成功完成且產生結果的 request 數。</summary>
    public int CompletedRequests { get; }

    /// <summary>取得 operation 失敗數。</summary>
    public int Errors { get; }

    /// <summary>取得 integrity failure 數。</summary>
    public int IntegrityFailures { get; }

    /// <summary>取得 run 是否因 cancellation 未完成 planned requests。</summary>
    public bool WasCancelled { get; }
}

/// <summary>以固定 concurrency 分批工作，於每個 request barrier 執行 checkpoint。</summary>
internal sealed class BoundedProbeCoordinator
{
    private static readonly int[] RequiredMemoryCheckpoints = { 10, 100, 500, 1000, 2500, 5000 };

    /// <summary>以固定 worker 上限執行 request，並在 sample interval 完成後等待整批 drain。</summary>
    public async Task<ProbeCoordinatorResult> ExecuteAsync(
        int requestCount,
        int concurrency,
        int sampleEvery,
        Func<int, int, CancellationToken, ValueTask<ProbeOperationSample>> executeRequest,
        Action<int, ProbeOperationSample> onSample,
        Action<int> onCheckpoint,
        CancellationToken cancellationToken = default,
        Action<int, Exception>? onError = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleEvery);
        ArgumentNullException.ThrowIfNull(executeRequest);
        ArgumentNullException.ThrowIfNull(onSample);
        ArgumentNullException.ThrowIfNull(onCheckpoint);

        var counters = new CoordinatorCounters();
        var wasCancelled = false;
        var plannedRequestsAtLastCheckpoint = 0;
        var lastCheckpointCount = 0;
        var requiredCheckpointIndex = 0;
        long nextPeriodicCheckpoint = sampleEvery;

        while (plannedRequestsAtLastCheckpoint < requestCount)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            while (requiredCheckpointIndex < RequiredMemoryCheckpoints.Length &&
                   RequiredMemoryCheckpoints[requiredCheckpointIndex] <= plannedRequestsAtLastCheckpoint)
            {
                requiredCheckpointIndex++;
            }

            var nextRequired = requiredCheckpointIndex < RequiredMemoryCheckpoints.Length
                ? RequiredMemoryCheckpoints[requiredCheckpointIndex]
                : int.MaxValue;
            var batchEnd = (int)Math.Min(
                requestCount,
                Math.Min(nextPeriodicCheckpoint, nextRequired));
            var batchCount = batchEnd - plannedRequestsAtLastCheckpoint;
            var batchCursor = new BatchCursor();
            var workerCount = Math.Min(concurrency, batchCount);
            var workerTasks = new Task[workerCount];
            for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                var stableWorkerIndex = workerIndex;
                workerTasks[workerIndex] = RunWorkerAsync(
                    plannedRequestsAtLastCheckpoint,
                    batchCount,
                    stableWorkerIndex,
                    batchCursor,
                    counters,
                    executeRequest,
                    onSample,
                    onError,
                    cancellationToken);
            }

            try
            {
                await Task.WhenAll(workerTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
            }

            wasCancelled |= cancellationToken.IsCancellationRequested;
            var terminalRequests = Volatile.Read(ref counters.CompletedRequests) + Volatile.Read(ref counters.Errors);
            plannedRequestsAtLastCheckpoint = batchEnd;
            if (terminalRequests > lastCheckpointCount)
            {
                onCheckpoint(terminalRequests);
                lastCheckpointCount = terminalRequests;
            }

            if (wasCancelled)
            {
                break;
            }

            while (nextPeriodicCheckpoint <= plannedRequestsAtLastCheckpoint)
            {
                nextPeriodicCheckpoint += sampleEvery;
            }
        }

        return new ProbeCoordinatorResult(
            Volatile.Read(ref counters.AttemptedRequests),
            Volatile.Read(ref counters.CompletedRequests),
            Volatile.Read(ref counters.Errors),
            Volatile.Read(ref counters.IntegrityFailures),
            wasCancelled);
    }

    /// <summary>以固定 worker slot 執行 batch request 並收集成功、error 與 integrity counters。</summary>
    private static async Task RunWorkerAsync(
        int batchStart,
        int batchCount,
        int workerIndex,
        BatchCursor cursor,
        CoordinatorCounters counters,
        Func<int, int, CancellationToken, ValueTask<ProbeOperationSample>> executeRequest,
        Action<int, ProbeOperationSample> onSample,
        Action<int, Exception>? onError,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var offset = cursor.TakeNextOffset();
            if (offset >= batchCount)
            {
                return;
            }

            var requestIndex = batchStart + offset;
            Interlocked.Increment(ref counters.AttemptedRequests);
            ProbeOperationSample sample;
            try
            {
                sample = await executeRequest(requestIndex, workerIndex, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref counters.Errors);
                onError?.Invoke(requestIndex, exception);
                continue;
            }

            Interlocked.Increment(ref counters.CompletedRequests);
            if (sample.IntegrityFailure)
            {
                Interlocked.Increment(ref counters.IntegrityFailures);
            }

            onSample(requestIndex, sample);
        }
    }

    /// <summary>提供批次內 thread-safe 的唯一 request offset dispatcher。</summary>
    private sealed class BatchCursor
    {
        private int _nextOffset;

        /// <summary>取得下一個 request offset。</summary>
        public int TakeNextOffset()
        {
            return Interlocked.Increment(ref _nextOffset) - 1;
        }
    }

    /// <summary>保存跨 worker 共用的 atomic request counters。</summary>
    private sealed class CoordinatorCounters
    {
        /// <summary>取得已開始執行的 request 數。</summary>
        public int AttemptedRequests;

        /// <summary>取得成功完成並產生結果的 request 數。</summary>
        public int CompletedRequests;

        /// <summary>取得 operation error 數。</summary>
        public int Errors;

        /// <summary>取得 integrity failure 數。</summary>
        public int IntegrityFailures;
    }
}
