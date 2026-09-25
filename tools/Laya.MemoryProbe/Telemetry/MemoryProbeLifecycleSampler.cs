using System.Diagnostics;

namespace Laya.MemoryProbe.Telemetry;

/// <summary>依序擷取 T0／T1／T2、warmup 與正式 request checkpoint。</summary>
internal sealed class MemoryProbeLifecycleSampler
{
    private readonly string _runId;
    private readonly string _scenario;
    private readonly Action<MemoryProbeSample> _writeSample;
    private readonly Func<ProcessMemorySnapshot> _captureMemory;
    private readonly long _startTimestamp;
    private int _sampleIndex;
    private bool _capturedT0;
    private bool _capturedT1;
    private bool _capturedT2;

    /// <summary>建立立即可擷取 T0 的 sampler，sample 透過 sink 即時交付。</summary>
    public MemoryProbeLifecycleSampler(
        Action<MemoryProbeSample> writeSample,
        string runId = "probe-run",
        string scenario = "unknown",
        Func<ProcessMemorySnapshot>? captureMemory = null)
    {
        ArgumentNullException.ThrowIfNull(writeSample);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        _runId = runId;
        _scenario = scenario;
        _writeSample = writeSample;
        _captureMemory = captureMemory ?? ProcessMemoryCollector.Capture;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>擷取 model／tokenizer 載入前 T0。</summary>
    public void CaptureT0BeforeModelLoad()
    {
        if (_capturedT0)
        {
            throw new InvalidOperationException("T0 sample can only be captured once.");
        }

        _capturedT0 = true;
        Emit("lifecycle", "t0-before-model-load", 0, 0, 0, 0, 0, 0);
    }

    /// <summary>擷取 model load 完成後 T1。</summary>
    public void CaptureT1AfterModelLoad()
    {
        CaptureT1AfterSetup(modelLoaded: true);
    }

    /// <summary>擷取 model load 或 component setup 完成後 T1，並標示適用口徑。</summary>
    public void CaptureT1AfterSetup(bool modelLoaded)
    {
        EnsureT0Captured();
        if (_capturedT1)
        {
            throw new InvalidOperationException("T1 sample can only be captured once.");
        }

        _capturedT1 = true;
        Emit(
            "setup",
            modelLoaded ? "t1-after-model-load" : "t1-after-setup",
            0,
            0,
            0,
            0,
            0,
            0);
    }

    /// <summary>擷取第一個 scenario operation 完成後 T2。</summary>
    public void CaptureT2AfterFirstOperation(
        int? sequenceLength,
        TimeSpan? inferenceDuration = null,
        bool? wasTruncated = null,
        int warmupRequests = 0,
        int totalOperationCount = 0)
    {
        EnsureT1Captured();
        if (_capturedT2)
        {
            throw new InvalidOperationException("T2 sample can only be captured once.");
        }

        _capturedT2 = true;
        Emit(
            "first-operation",
            "t2-after-first-operation",
            0,
            0,
            0,
            0,
            warmupRequests,
            totalOperationCount,
            sequenceLength: sequenceLength,
            inferenceDuration: inferenceDuration,
            wasTruncated: wasTruncated);
    }

    /// <summary>擷取 warmup 結束且正式 request count 尚為零的基準樣本。</summary>
    public void CaptureWarmupComplete(int warmupRequests, int totalOperationCount)
    {
        EnsureT0Captured();
        if (warmupRequests < 0 || totalOperationCount < warmupRequests)
        {
            throw new ArgumentOutOfRangeException(nameof(warmupRequests));
        }

        Emit(
            "warmup",
            "warmup-complete",
            0,
            0,
            0,
            0,
            warmupRequests,
            totalOperationCount);
    }

    /// <summary>擷取 barrier 完成後的實際 request counters 與 latency summary。</summary>
    public void CaptureRequestCheckpoint(
        int attemptedRequests,
        int completedRequests,
        int errors,
        int integrityFailures,
        int warmupRequests,
        int totalOperationCount,
        TimeSpan? lastLatency = null,
        TimeSpan? meanLatency = null,
        TimeSpan? p50Latency = null,
        TimeSpan? p95Latency = null,
        TimeSpan? p99Latency = null,
        TimeSpan? inferenceDuration = null,
        int? sequenceLength = null,
        bool? wasTruncated = null,
        ProcessMemorySnapshot? memorySnapshot = null)
    {
        EnsureT0Captured();
        if (attemptedRequests < 0 || completedRequests < 0 || errors < 0 || integrityFailures < 0 ||
            completedRequests > attemptedRequests || totalOperationCount < warmupRequests + attemptedRequests)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptedRequests), "Checkpoint counts are inconsistent.");
        }

        Emit(
            "measurement",
            $"request-count-{completedRequests}",
            attemptedRequests,
            completedRequests,
            errors,
            integrityFailures,
            warmupRequests,
            totalOperationCount,
            lastLatency,
            meanLatency,
            p50Latency,
            p95Latency,
            p99Latency,
            inferenceDuration,
            sequenceLength,
            wasTruncated,
            memorySnapshot);
    }

    /// <summary>擷取 idle、forced-GC 或 session lifecycle 等診斷 checkpoint。</summary>
    public void CaptureDiagnosticCheckpoint(
        string phase,
        string reason,
        int attemptedRequests,
        int completedRequests,
        int errors,
        int integrityFailures,
        int warmupRequests,
        int totalOperationCount,
        ProcessMemorySnapshot? memorySnapshot = null)
    {
        EnsureT0Captured();
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Emit(
            phase,
            reason,
            attemptedRequests,
            completedRequests,
            errors,
            integrityFailures,
            warmupRequests,
            totalOperationCount,
            memorySnapshot: memorySnapshot);
    }

    /// <summary>確保 lifecycle 量測先擷取 T0。</summary>
    private void EnsureT0Captured()
    {
        if (!_capturedT0)
        {
            throw new InvalidOperationException("Capture T0 before later lifecycle samples.");
        }
    }

    /// <summary>確保 T2 之前已擷取 setup／model-load 後的 T1。</summary>
    private void EnsureT1Captured()
    {
        EnsureT0Captured();
        if (!_capturedT1)
        {
            throw new InvalidOperationException("Capture T1 before the first operation sample.");
        }
    }

    /// <summary>建立 sample 並立即交給 sink，不在 sampler 內累積 raw records。</summary>
    private void Emit(
        string phase,
        string reason,
        int attemptedRequests,
        int completedRequests,
        int errors,
        int integrityFailures,
        int warmupRequests,
        int totalOperationCount,
        TimeSpan? lastLatency = null,
        TimeSpan? meanLatency = null,
        TimeSpan? p50Latency = null,
        TimeSpan? p95Latency = null,
        TimeSpan? p99Latency = null,
        TimeSpan? inferenceDuration = null,
        int? sequenceLength = null,
        bool? wasTruncated = null,
        ProcessMemorySnapshot? memorySnapshot = null)
    {
        _writeSample(new MemoryProbeSample(
            _sampleIndex++,
            _runId,
            _scenario,
            DateTimeOffset.UtcNow,
            Stopwatch.GetElapsedTime(_startTimestamp),
            phase,
            reason,
            completedRequests,
            attemptedRequests,
            completedRequests,
            errors,
            integrityFailures,
            warmupRequests,
            totalOperationCount,
            lastLatency,
            meanLatency,
            p50Latency,
            p95Latency,
            p99Latency,
            inferenceDuration,
            sequenceLength,
            wasTruncated,
            memorySnapshot ?? _captureMemory()));
    }
}
