using System.Security.Cryptography;
using System.Text.Json;
using Laya.Core.Configuration;
using Laya.Core.Inference;
using Laya.MemoryProbe.Configuration;
using Laya.MemoryProbe.Scenarios;
using Laya.MemoryProbe.Telemetry;
using Laya.Shared.Configuration;

namespace Laya.MemoryProbe.Reporting;

/// <summary>協調單一 process probe lifecycle、sampling、raw evidence 與 run manifest。</summary>
internal static class MemoryProbeRunHost
{
    /// <summary>執行 pilot/formal 單一 run，失敗時保留 incomplete manifest 與已 flush samples。</summary>
    public static async Task<int> ExecuteAsync(
        MemoryProbeOptions options,
        LayaProfileResolution profile,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var (workloadPath, workloadManifestPath) = ResolveWorkloadPaths(repositoryRoot);
        using var catalog = MemoryWorkloadCatalog.Load(workloadPath, workloadManifestPath);
        using var writer = new MemoryProbeRunWriter(options.OutputDirectory, options.RunId, GetScenarioName(options));
        var sourceIdentity = MemoryProbeHostMetadata.CaptureSourceIdentity(repositoryRoot);
        var environment = MemoryProbeHostMetadata.CaptureEnvironment();
        var manifest = MemoryProbeRunManifestBuilder.Build(
            options,
            profile,
            workloadManifestPath,
            sourceIdentity,
            environment,
            DateTimeOffset.UtcNow);
        writer.WriteManifest(manifest);

        var sampler = new MemoryProbeLifecycleSampler(
            sample => writer.WriteSample(sample),
            options.RunId,
            GetScenarioName(options));
        var counters = new RunCounters(options.WarmupRequests, options.Requests);

        try
        {
            sampler.CaptureT0BeforeModelLoad();
            if (options.IsLoadOnly)
            {
                ExecuteLoadOnly(options, profile, sampler);
            }
            else
            {
                await ExecuteScenarioAsync(
                    options,
                    profile,
                    catalog,
                    sampler,
                    writer,
                    manifest,
                    counters,
                    cancellationToken).ConfigureAwait(false);
            }

            var isCancelled = cancellationToken.IsCancellationRequested;
            writer.WriteSummary(CreateRunSummary(options, counters, isCancelled ? "incomplete" : "complete"));
            UpdateManifest(
                manifest,
                writer,
                counters,
                status: isCancelled ? "incomplete" : "complete",
                terminalOutcome: isCancelled ? "cancelled" : "completed",
                failure: null);
            writer.WriteManifest(manifest);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                runId = options.RunId,
                status = manifest["status"],
                completedRequests = counters.CompletedRequests,
                errors = counters.Errors,
                runDirectory = writer.RunDirectory
            }));
            return isCancelled ? 2 : 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            writer.WriteSummary(CreateRunSummary(options, counters, "incomplete"));
            UpdateManifest(
                manifest,
                writer,
                counters,
                status: "incomplete",
                terminalOutcome: exception is OperationCanceledException ? "cancelled" : "probe-error",
                failure: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = exception.GetType().FullName,
                    ["message"] = exception.Message
                });
            writer.WriteManifest(manifest);
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    /// <summary>載入 ONNX session 並輸出 T1／request=0／post-dispose memory evidence。</summary>
    private static void ExecuteLoadOnly(
        MemoryProbeOptions options,
        LayaProfileResolution profile,
        MemoryProbeLifecycleSampler sampler)
    {
        using (LayaOnnxSession.Open(CreateLayaOptions(options, profile)))
        {
            sampler.CaptureT1AfterModelLoad();
            sampler.CaptureWarmupComplete(0, 0);
            sampler.CaptureRequestCheckpoint(0, 0, 0, 0, 0, 0);
        }

        sampler.CaptureDiagnosticCheckpoint("lifecycle", "post-dispose", 0, 0, 0, 0, 0, 0);
    }

    /// <summary>執行 scenario warmup、正式 request barriers、GC controls 與 idle stabilization。</summary>
    private static async Task ExecuteScenarioAsync(
        MemoryProbeOptions options,
        LayaProfileResolution profile,
        MemoryWorkloadCatalog catalog,
        MemoryProbeLifecycleSampler sampler,
        MemoryProbeRunWriter writer,
        Dictionary<string, object?> manifest,
        RunCounters counters,
        CancellationToken cancellationToken)
    {
        using var execution = MemoryProbeScenarioExecution.Create(options, profile, catalog);
        sampler.CaptureT1AfterSetup(execution.HasInferenceSession);

        for (var warmupIndex = 0; warmupIndex < options.WarmupRequests; warmupIndex++)
        {
            var warmupSample = execution.ExecuteSample(warmupIndex);
            Interlocked.Increment(ref counters.WarmupRequests);
            counters.TotalOperationCount++;
            if (warmupIndex == 0)
            {
                sampler.CaptureT2AfterFirstOperation(
                    warmupSample.SequenceLength,
                    warmupSample.InferenceDuration,
                    warmupSample.WasTruncated,
                    warmupRequests: counters.WarmupRequests,
                    totalOperationCount: counters.TotalOperationCount);
            }
        }

        sampler.CaptureWarmupComplete(counters.WarmupRequests, counters.TotalOperationCount);
        sampler.CaptureRequestCheckpoint(0, 0, 0, 0, counters.WarmupRequests, counters.TotalOperationCount);

        var latencyWindow = new BoundedLatencyWindow(Math.Min(options.SampleEvery, options.Requests));
        var wholeRunLatencyWindow = new BoundedLatencyWindow(options.Requests);
        var latencyWindowLock = new object();
        var latestSampleLock = new object();
        ProbeOperationSample? latestSample = null;
        var firstMeasuredOperation = options.WarmupRequests == 0 ? 0 : 1;
        var coordinator = new BoundedProbeCoordinator();

        var result = await coordinator.ExecuteAsync(
            options.Requests,
            options.Concurrency,
            options.SampleEvery,
            (requestIndex, workerIndex, workerToken) =>
            {
                Interlocked.Increment(ref counters.AttemptedRequests);
                workerToken.ThrowIfCancellationRequested();
                var sample = execution.ExecuteSample(
                    requestIndex,
                    workerIndex,
                    (cycleIndex, phase, memory) => sampler.CaptureDiagnosticCheckpoint(
                        "session-lifecycle",
                        $"session-cycle-{cycleIndex}-{phase}",
                        cycleIndex,
                        cycleIndex,
                        0,
                        0,
                        counters.WarmupRequests,
                        counters.WarmupRequests + cycleIndex,
                        memory));
                return ValueTask.FromResult(sample);
            },
            (requestIndex, sample) =>
            {
                Interlocked.Increment(ref counters.CompletedRequests);
                if (sample.IntegrityFailure)
                {
                    Interlocked.Increment(ref counters.IntegrityFailures);
                }

                lock (latencyWindowLock)
                {
                    latencyWindow.Add(sample.EndToEndDuration);
                    wholeRunLatencyWindow.Add(sample.EndToEndDuration);
                }

                writer.WriteLatency(requestIndex, sample);
                lock (latestSampleLock)
                {
                    latestSample = sample;
                }

                if (Interlocked.CompareExchange(ref firstMeasuredOperation, 1, 0) == 0)
                {
                    sampler.CaptureT2AfterFirstOperation(
                        sample.SequenceLength,
                        sample.InferenceDuration,
                        sample.WasTruncated,
                        warmupRequests: counters.WarmupRequests,
                        totalOperationCount: counters.WarmupRequests + 1);
                }
            },
            terminalRequests =>
            {
                MemoryLatencySummary? summary;
                lock (latencyWindowLock)
                {
                    summary = latencyWindow.SnapshotAndReset();
                }

                ProbeOperationSample? lastSample;
                lock (latestSampleLock)
                {
                    lastSample = latestSample;
                }

                var attempted = Volatile.Read(ref counters.AttemptedRequests);
                var completed = Volatile.Read(ref counters.CompletedRequests);
                var errors = Volatile.Read(ref counters.Errors);
                var integrityFailures = Volatile.Read(ref counters.IntegrityFailures);
                sampler.CaptureRequestCheckpoint(
                    attempted,
                    completed,
                    errors,
                    integrityFailures,
                    counters.WarmupRequests,
                    counters.WarmupRequests + attempted,
                    ToTimeSpan(summary?.LastMilliseconds),
                    ToTimeSpan(summary?.MeanMilliseconds),
                    ToTimeSpan(summary?.P50Milliseconds),
                    ToTimeSpan(summary?.P95Milliseconds),
                    ToTimeSpan(summary?.P99Milliseconds),
                    lastSample?.InferenceDuration,
                    lastSample?.SequenceLength,
                    lastSample?.WasTruncated);
                UpdateManifest(
                    manifest,
                    writer,
                    counters,
                    status: "in-progress",
                    terminalOutcome: "in-progress",
                    failure: null,
                    terminal: false,
                    includeArtifactHashes: false);
                writer.WriteManifest(manifest);

                if (options.GcCheckpoints.Contains(attempted))
                {
                    MemoryProbeDiagnostics.ForceFullGc(reason => sampler.CaptureDiagnosticCheckpoint(
                        "forced-gc",
                        reason,
                        attempted,
                        completed,
                        errors,
                        integrityFailures,
                        counters.WarmupRequests,
                        counters.WarmupRequests + attempted));
                }

                if (terminalRequests != completed + errors)
                {
                    throw new InvalidOperationException("Coordinator checkpoint counts did not match completed outcomes.");
                }
            },
            cancellationToken,
            (requestIndex, exception) =>
            {
                Interlocked.Increment(ref counters.Errors);
                writer.WriteError(requestIndex, exception);
            }).ConfigureAwait(false);

        counters.WasCancelled = result.WasCancelled;
        counters.LatencySummary = wholeRunLatencyWindow.SnapshotAndReset();
        counters.Checksum = execution.Checksum;
        await ObservePostRunIdleAsync(options, sampler, counters, cancellationToken).ConfigureAwait(false);
        execution.Dispose();
        sampler.CaptureDiagnosticCheckpoint(
            "lifecycle",
            "post-dispose",
            counters.AttemptedRequests,
            counters.CompletedRequests,
            counters.Errors,
            counters.IntegrityFailures,
            counters.WarmupRequests,
            counters.TotalOperationCount + counters.AttemptedRequests);
    }

    /// <summary>執行 idle interval；在第一段 idle 後另行採集明確 forced-GC 前後樣本。</summary>
    private static async Task ObservePostRunIdleAsync(
        MemoryProbeOptions options,
        MemoryProbeLifecycleSampler sampler,
        RunCounters counters,
        CancellationToken cancellationToken)
    {
        if (options.IdleSeconds.Count == 0)
        {
            return;
        }

        var attempted = Volatile.Read(ref counters.AttemptedRequests);
        var warmupRequests = Volatile.Read(ref counters.WarmupRequests);
        var completed = Volatile.Read(ref counters.CompletedRequests);
        var errors = Volatile.Read(ref counters.Errors);
        var integrityFailures = Volatile.Read(ref counters.IntegrityFailures);
        var firstInterval = new[] { options.IdleSeconds[0] };
        await MemoryProbeDiagnostics.ObserveIdleAsync(
            firstInterval,
            static (duration, token) => new ValueTask(Task.Delay(duration, token)),
            seconds => sampler.CaptureDiagnosticCheckpoint(
                "idle",
                $"idle-{seconds}-seconds",
                attempted,
                completed,
                errors,
                integrityFailures,
                warmupRequests,
                warmupRequests + attempted),
            cancellationToken).ConfigureAwait(false);

        if (options.IdleSeconds[0] > 0)
        {
            MemoryProbeDiagnostics.ForceFullGc(reason => sampler.CaptureDiagnosticCheckpoint(
                "forced-gc",
                reason,
                attempted,
                completed,
                errors,
                integrityFailures,
                warmupRequests,
                warmupRequests + attempted));
        }

        if (options.IdleSeconds.Count > 1)
        {
            await MemoryProbeDiagnostics.ObserveIdleAsync(
                options.IdleSeconds.Skip(1).ToArray(),
                static (duration, token) => new ValueTask(Task.Delay(duration, token)),
                seconds => sampler.CaptureDiagnosticCheckpoint(
                    "idle",
                    $"idle-{seconds}-seconds",
                    attempted,
                    completed,
                    errors,
                    integrityFailures,
                    warmupRequests,
                    warmupRequests + attempted),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>依 build output 或 repository 路徑解析固定 workload fixture。</summary>
    private static (string WorkloadPath, string ManifestPath) ResolveWorkloadPaths(string repositoryRoot)
    {
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "test-data", "phase3a");
        var repositoryDirectory = Path.Combine(repositoryRoot, "test-data", "phase3a");
        var workloadPath = Path.Combine(outputDirectory, "memory-workloads.json");
        var manifestPath = Path.Combine(outputDirectory, "memory-workloads-manifest.json");
        if (!File.Exists(workloadPath) || !File.Exists(manifestPath))
        {
            workloadPath = Path.Combine(repositoryDirectory, "memory-workloads.json");
            manifestPath = Path.Combine(repositoryDirectory, "memory-workloads-manifest.json");
        }

        return (workloadPath, manifestPath);
    }

    /// <summary>建立包含指定模型身分與 arena 的 Core options。</summary>
    private static LayaOptions CreateLayaOptions(MemoryProbeOptions options, LayaProfileResolution profile)
    {
        return new LayaOptions(
            profile.ModelRoot,
            profile.Name,
            profile.CheckpointRevision,
            enableCpuMemArena: options.CpuArenaEnabled);
    }

    /// <summary>依 nullable milliseconds 建立 latency TimeSpan。</summary>
    private static TimeSpan? ToTimeSpan(double? milliseconds)
    {
        return milliseconds.HasValue ? TimeSpan.FromMilliseconds(milliseconds.Value) : null;
    }

    /// <summary>取得 run manifest 所需 scenario 字串。</summary>
    private static string GetScenarioName(MemoryProbeOptions options)
    {
        if (options.IsLoadOnly)
        {
            return "load-only";
        }

        return options.Scenario switch
        {
            MemoryProbeScenario.TokenizerOnly => "tokenizer-only",
            MemoryProbeScenario.SequenceOnly => "sequence-only",
            MemoryProbeScenario.TensorOnly => "tensor-only",
            MemoryProbeScenario.RunOnly => "run-only",
            MemoryProbeScenario.CalibrationOnly => "calibration-only",
            MemoryProbeScenario.FullPipeline => "full-pipeline",
            MemoryProbeScenario.SessionRecreate => "session-recreate",
            _ => throw new ArgumentOutOfRangeException(nameof(options))
        };
    }

    /// <summary>更新 actual counts、terminal outcome 與 raw artifact hashes。</summary>
    private static void UpdateManifest(
        Dictionary<string, object?> manifest,
        MemoryProbeRunWriter writer,
        RunCounters counters,
        string status,
        string terminalOutcome,
        object? failure,
        bool terminal = true,
        bool includeArtifactHashes = true)
    {
        manifest["status"] = status;
        manifest["terminalOutcome"] = terminalOutcome;
        manifest["endedUtc"] = terminal
            ? DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            : null;
        manifest["failure"] = failure;
        manifest["actualCounts"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["warmupRequests"] = Volatile.Read(ref counters.WarmupRequests),
            ["attemptedRequests"] = counters.AttemptedRequests,
            ["completedRequests"] = counters.CompletedRequests,
            ["errors"] = counters.Errors,
            ["integrityFailures"] = counters.IntegrityFailures
        };
        if (includeArtifactHashes)
        {
            manifest["artifacts"] = GetArtifactIndex(writer);
        }
    }

    /// <summary>建立由 raw operation samples 計算的 per-run latency summary JSON。</summary>
    private static Dictionary<string, object?> CreateRunSummary(
        MemoryProbeOptions options,
        RunCounters counters,
        string status)
    {
        var latency = counters.LatencySummary is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sampleCount"] = counters.LatencySummary.SampleCount,
                ["meanMilliseconds"] = counters.LatencySummary.MeanMilliseconds,
                ["p50Milliseconds"] = counters.LatencySummary.P50Milliseconds,
                ["p95Milliseconds"] = counters.LatencySummary.P95Milliseconds,
                ["p99Milliseconds"] = counters.LatencySummary.P99Milliseconds,
                ["minMilliseconds"] = counters.LatencySummary.MinMilliseconds,
                ["maxMilliseconds"] = counters.LatencySummary.MaxMilliseconds,
                ["lastMilliseconds"] = counters.LatencySummary.LastMilliseconds
            };
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["runId"] = options.RunId,
            ["scenario"] = GetScenarioName(options),
            ["mode"] = options.Mode == MemoryProbeMode.Pilot ? "pilot" : "formal",
            ["status"] = status,
            ["plannedRequests"] = options.Requests,
            ["warmupRequests"] = Volatile.Read(ref counters.WarmupRequests),
            ["attemptedRequests"] = Volatile.Read(ref counters.AttemptedRequests),
            ["completedRequests"] = Volatile.Read(ref counters.CompletedRequests),
            ["errors"] = Volatile.Read(ref counters.Errors),
            ["integrityFailures"] = Volatile.Read(ref counters.IntegrityFailures),
            ["latency"] = latency,
            ["checksum"] = counters.Checksum
        };
    }

    /// <summary>取得已 flush raw files 的 bytes／SHA-256 artifact index。</summary>
    private static IReadOnlyList<Dictionary<string, object?>> GetArtifactIndex(MemoryProbeRunWriter writer)
    {
        return new[] { writer.SamplesPath, writer.LatencyPath, writer.ErrorsPath, writer.SummaryPath }
            .Where(File.Exists)
            .Select(path => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = Path.GetFileName(path),
                ["sha256"] = HashFile(path),
                ["sizeBytes"] = new FileInfo(path).Length,
                ["status"] = "complete",
                ["unavailableReason"] = null
            })
            .ToArray();
    }

    /// <summary>計算 raw artifact 檔案的 SHA-256。</summary>
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>保存 run request counters，供 worker、sampler 與 manifest 共用。</summary>
    private sealed class RunCounters
    {
        /// <summary>建立已驗證的 planned warmup 與 request counts。</summary>
        public RunCounters(int plannedWarmupRequests, int plannedRequests)
        {
            PlannedWarmupRequests = plannedWarmupRequests;
            PlannedRequests = plannedRequests;
        }

        /// <summary>取得 planned warmup request 數。</summary>
        public int PlannedWarmupRequests { get; }

        /// <summary>取得實際成功完成的 warmup request 數。</summary>
        public int WarmupRequests;

        /// <summary>取得 planned formal requests。</summary>
        public int PlannedRequests { get; }

        /// <summary>取得實際開始數。</summary>
        public int AttemptedRequests;

        /// <summary>取得成功完成數。</summary>
        public int CompletedRequests;

        /// <summary>取得 operation errors。</summary>
        public int Errors;

        /// <summary>取得 integrity failures。</summary>
        public int IntegrityFailures;

        /// <summary>取得取消狀態。</summary>
        public bool WasCancelled;

        /// <summary>取得已執行 warmup operation 數。</summary>
        public int TotalOperationCount;

        /// <summary>取得全 run raw latency summary。</summary>
        public MemoryLatencySummary? LatencySummary;

        /// <summary>取得 scenario output checksum。</summary>
        public double Checksum;
    }
}
