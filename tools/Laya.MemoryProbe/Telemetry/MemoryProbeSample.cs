namespace Laya.MemoryProbe.Telemetry;

/// <summary>保存單一 lifecycle 或 request checkpoint 的 raw memory sample。</summary>
internal sealed class MemoryProbeSample
{
    /// <summary>建立包含 process、GC、request 與 latency 身分的 sample。</summary>
    public MemoryProbeSample(
        int sampleIndex,
        string runId,
        string scenario,
        DateTimeOffset timestampUtc,
        TimeSpan elapsed,
        string phase,
        string reason,
        int requestCount,
        int attemptedRequests,
        int completedRequests,
        int errors,
        int integrityFailures,
        int warmupRequests,
        int totalOperationCount,
        TimeSpan? lastLatency,
        TimeSpan? meanLatency,
        TimeSpan? p50Latency,
        TimeSpan? p95Latency,
        TimeSpan? p99Latency,
        TimeSpan? lastInferenceDuration,
        int? sequenceLength,
        bool? wasTruncated,
        ProcessMemorySnapshot memory)
    {
        SampleIndex = sampleIndex;
        RunId = runId;
        Scenario = scenario;
        TimestampUtc = timestampUtc;
        Elapsed = elapsed;
        Phase = phase;
        Reason = reason;
        RequestCount = requestCount;
        AttemptedRequests = attemptedRequests;
        CompletedRequests = completedRequests;
        Errors = errors;
        IntegrityFailures = integrityFailures;
        WarmupRequests = warmupRequests;
        TotalOperationCount = totalOperationCount;
        LastLatency = lastLatency;
        MeanLatency = meanLatency;
        P50Latency = p50Latency;
        P95Latency = p95Latency;
        P99Latency = p99Latency;
        LastInferenceDuration = lastInferenceDuration;
        SequenceLength = sequenceLength;
        WasTruncated = wasTruncated;
        Memory = memory;
    }

    /// <summary>取得 run 內 sample 順序索引。</summary>
    public int SampleIndex { get; }

    /// <summary>取得唯一 run ID。</summary>
    public string RunId { get; }

    /// <summary>取得 scenario 名稱。</summary>
    public string Scenario { get; }

    /// <summary>取得 UTC wall-clock timestamp。</summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>取得 process 內 monotonic elapsed。</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>取得 lifecycle／measurement phase。</summary>
    public string Phase { get; }

    /// <summary>取得此次 sample 的 checkpoint reason。</summary>
    public string Reason { get; }

    /// <summary>取得 warmup 後成功完成的正式 request 數。</summary>
    public int RequestCount { get; }

    /// <summary>取得實際開始執行的 request 數。</summary>
    public int AttemptedRequests { get; }

    /// <summary>取得成功完成並產生結果的 request 數。</summary>
    public int CompletedRequests { get; }

    /// <summary>取得 operation error count。</summary>
    public int Errors { get; }

    /// <summary>取得 result integrity failure count。</summary>
    public int IntegrityFailures { get; }

    /// <summary>取得不列入正式 requests 的 warmup 數。</summary>
    public int WarmupRequests { get; }

    /// <summary>取得 warmup 與正式 request 合計操作數。</summary>
    public int TotalOperationCount { get; }

    /// <summary>取得最近一筆 end-to-end latency。</summary>
    public TimeSpan? LastLatency { get; }

    /// <summary>取得目前 run end-to-end mean latency。</summary>
    public TimeSpan? MeanLatency { get; }

    /// <summary>取得目前 run end-to-end P50 latency。</summary>
    public TimeSpan? P50Latency { get; }

    /// <summary>取得目前 run end-to-end P95 latency。</summary>
    public TimeSpan? P95Latency { get; }

    /// <summary>取得目前 run end-to-end P99 latency。</summary>
    public TimeSpan? P99Latency { get; }

    /// <summary>取得最近一筆 native inference duration。</summary>
    public TimeSpan? LastInferenceDuration { get; }

    /// <summary>取得最近 request 的實際 sequence length。</summary>
    public int? SequenceLength { get; }

    /// <summary>取得最近 request 是否被截斷。</summary>
    public bool? WasTruncated { get; }

    /// <summary>取得 process／GC／Linux proc counters。</summary>
    public ProcessMemorySnapshot Memory { get; }
}
