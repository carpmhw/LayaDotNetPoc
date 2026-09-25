using System.Globalization;
using System.Text;
using System.Text.Json;
using Laya.MemoryProbe.Scenarios;
using Laya.MemoryProbe.Telemetry;

namespace Laya.MemoryProbe.Reporting;

/// <summary>以逐筆 flush、唯一 run directory 寫入 Phase 3A raw CSV 與 manifest。</summary>
internal sealed class MemoryProbeRunWriter : IDisposable
{
    private static readonly string[] SampleHeaders =
    {
        "sample_index", "run_id", "scenario", "timestamp_utc", "elapsed_ms", "phase", "reason",
        "request_count", "attempted_requests", "completed_requests", "errors", "integrity_failures",
        "warmup_requests", "total_operation_count", "working_set_bytes", "private_memory_bytes",
        "virtual_memory_bytes", "handle_count", "threads", "managed_heap_bytes", "managed_heap_unavailable_reason", "heap_size_bytes",
        "fragmented_bytes", "gc_index", "gen0", "gen1", "gen2", "total_allocated_bytes",
        "vmrss_bytes", "vmrss_unavailable_reason", "vmsize_bytes", "vmsize_unavailable_reason",
        "vmdata_bytes", "vmdata_unavailable_reason", "rss_anon_bytes", "rss_anon_unavailable_reason",
        "rss_file_bytes", "rss_file_unavailable_reason", "rss_shmem_bytes", "rss_shmem_unavailable_reason",
        "last_latency_ms", "mean_latency_ms", "p50_latency_ms", "p95_latency_ms", "p99_latency_ms",
        "last_inference_ms", "sequence_length", "was_truncated"
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly FileStream _runLock;
    private StreamWriter? _samplesWriter;
    private StreamWriter? _latencyWriter;
    private StreamWriter? _errorsWriter;
    private int _disposed;

    /// <summary>建立全新 run directory 與唯一 CSV writers；不會覆寫既有 run。</summary>
    public MemoryProbeRunWriter(string outputRoot, string runId, string scenario)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

        var runsRoot = Path.Combine(Path.GetFullPath(outputRoot), "runs");
        Directory.CreateDirectory(runsRoot);
        RunDirectory = Path.Combine(runsRoot, runId);
        if (Directory.Exists(RunDirectory) || File.Exists(RunDirectory))
        {
            throw new IOException($"Phase 3A run directory already exists and cannot be overwritten: {RunDirectory}");
        }

        Directory.CreateDirectory(RunDirectory);
        _runLock = new FileStream(
            Path.Combine(RunDirectory, ".run-lock"),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        SamplesPath = Path.Combine(RunDirectory, "phase3a-memory-samples.csv");
        LatencyPath = Path.Combine(RunDirectory, "phase3a-memory-latency.csv");
        ErrorsPath = Path.Combine(RunDirectory, "phase3a-memory-errors.csv");
        SummaryPath = Path.Combine(RunDirectory, "summary.json");
        ManifestPath = Path.Combine(RunDirectory, "manifest.json");
        _samplesWriter = CreateWriter(SamplesPath);
        _latencyWriter = CreateWriter(LatencyPath);
        _errorsWriter = CreateWriter(ErrorsPath);
        WriteCsvRow(_samplesWriter, SampleHeaders);
        WriteCsvRow(_latencyWriter, new[]
        {
            "request_index", "end_to_end_latency_ms", "inference_latency_ms", "integrity_failure"
        });
        WriteCsvRow(_errorsWriter, new[] { "request_index", "exception_type", "message" });
    }

    /// <summary>取得此 run 的唯一輸出目錄。</summary>
    public string RunDirectory { get; }

    /// <summary>取得逐筆 process／GC memory samples CSV 路徑。</summary>
    public string SamplesPath { get; }

    /// <summary>取得逐 request raw latency CSV 路徑。</summary>
    public string LatencyPath { get; }

    /// <summary>取得單一 request error 診斷 CSV 路徑。</summary>
    public string ErrorsPath { get; }

    /// <summary>取得 machine-readable final run summary 路徑。</summary>
    public string SummaryPath { get; }

    /// <summary>取得 atomic replacement run manifest 路徑。</summary>
    public string ManifestPath { get; }

    /// <summary>序列化 manifest 至同目錄暫存檔後 atomic replace。</summary>
    public void WriteManifest<T>(T manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        EnsureOpen();
        lock (_sync)
        {
            EnsureOpen();
            var temporaryPath = ManifestPath + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, manifest, ManifestJsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, ManifestPath, overwrite: true);
        }
    }

    /// <summary>寫入唯一 machine-readable summary 並 flush 至 disk。</summary>
    public void WriteSummary<T>(T summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        lock (_sync)
        {
            EnsureOpen();
            var temporaryPath = SummaryPath + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, summary, ManifestJsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, SummaryPath, overwrite: true);
        }
    }

    /// <summary>逐筆序列化 process／GC／Linux／latency snapshot 並立即 flush 至磁碟。</summary>
    public void WriteSample(MemoryProbeSample sample, LinuxProcStatusSnapshot? linuxOverride = null)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_sync)
        {
            EnsureOpen();
            var linux = linuxOverride ?? sample.Memory.Linux;
            WriteCsvRow(_samplesWriter!, new[]
            {
            FormatInt(sample.SampleIndex),
            sample.RunId,
            sample.Scenario,
            sample.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            FormatDouble(sample.Elapsed.TotalMilliseconds),
            sample.Phase,
            sample.Reason,
            FormatInt(sample.RequestCount),
            FormatInt(sample.AttemptedRequests),
            FormatInt(sample.CompletedRequests),
            FormatInt(sample.Errors),
            FormatInt(sample.IntegrityFailures),
            FormatInt(sample.WarmupRequests),
            FormatInt(sample.TotalOperationCount),
            FormatLong(sample.Memory.WorkingSetBytes),
            FormatLong(sample.Memory.PrivateMemoryBytes),
            FormatLong(sample.Memory.VirtualMemoryBytes),
            FormatInt(sample.Memory.HandleCount),
            FormatInt(sample.Memory.ThreadCount),
            FormatNullableLong(sample.Memory.ManagedHeap.Bytes),
            sample.Memory.ManagedHeap.UnavailableReason,
            FormatLong(sample.Memory.HeapSizeBytes),
            FormatLong(sample.Memory.FragmentedBytes),
            FormatLong(sample.Memory.GcIndex),
            FormatInt(sample.Memory.Generation0Count),
            FormatInt(sample.Memory.Generation1Count),
            FormatInt(sample.Memory.Generation2Count),
            FormatLong(sample.Memory.TotalAllocatedBytes),
            FormatNullableLong(linux.VmRss.Bytes),
            linux.VmRss.UnavailableReason,
            FormatNullableLong(linux.VmSize.Bytes),
            linux.VmSize.UnavailableReason,
            FormatNullableLong(linux.VmData.Bytes),
            linux.VmData.UnavailableReason,
            FormatNullableLong(linux.RssAnon.Bytes),
            linux.RssAnon.UnavailableReason,
            FormatNullableLong(linux.RssFile.Bytes),
            linux.RssFile.UnavailableReason,
            FormatNullableLong(linux.RssShmem.Bytes),
            linux.RssShmem.UnavailableReason,
            FormatMilliseconds(sample.LastLatency),
            FormatMilliseconds(sample.MeanLatency),
            FormatMilliseconds(sample.P50Latency),
            FormatMilliseconds(sample.P95Latency),
            FormatMilliseconds(sample.P99Latency),
            FormatMilliseconds(sample.LastInferenceDuration),
            FormatNullableInt(sample.SequenceLength),
                sample.WasTruncated?.ToString().ToLowerInvariant()
            });
        }
    }

    /// <summary>逐筆 flush 不含 transaction 原文的 raw latency 與 integrity 結果。</summary>
    public void WriteLatency(int requestIndex, ProbeOperationSample sample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requestIndex);
        ArgumentNullException.ThrowIfNull(sample);
        lock (_sync)
        {
            EnsureOpen();
            WriteCsvRow(_latencyWriter!, new[]
            {
                FormatInt(requestIndex),
                FormatDouble(sample.EndToEndDuration.TotalMilliseconds),
                FormatMilliseconds(sample.InferenceDuration),
                sample.IntegrityFailure.ToString().ToLowerInvariant()
            });
        }
    }

    /// <summary>逐筆保存不含 transaction 原文的 request exception 診斷。</summary>
    public void WriteError(int requestIndex, Exception exception)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requestIndex);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            EnsureOpen();
            WriteCsvRow(_errorsWriter!, new[]
            {
                FormatInt(requestIndex),
                exception.GetType().FullName,
                exception.Message
            });
        }
    }

    /// <summary>釋放 writers 與 run lock，保留 raw files 供中斷 run 診斷。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_sync)
        {
            _samplesWriter?.Dispose();
            _samplesWriter = null;
            _latencyWriter?.Dispose();
            _latencyWriter = null;
            _errorsWriter?.Dispose();
            _errorsWriter = null;
            _runLock.Dispose();
        }
    }

    /// <summary>建立 UTF-8 no-BOM、逐列 flush 且不得覆寫的 CSV writer。</summary>
    private static StreamWriter CreateWriter(string path)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    /// <summary>輸出一列並套用 RFC 4180 quote escaping。</summary>
    private static void WriteCsvRow(TextWriter writer, IEnumerable<string?> values)
    {
        writer.WriteLine(string.Join(',', values.Select(EscapeCsvValue)));
        writer.Flush();
    }

    /// <summary>跳脫逗號、引號與換行，null 欄位輸出空字串。</summary>
    private static string EscapeCsvValue(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>以 invariant culture 格式化 nullable 整數或 bytes。</summary>
    private static string FormatLong(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>以 invariant culture 格式化 nullable bytes counter。</summary>
    private static string? FormatNullableLong(long? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>以 invariant culture 格式化整數 counter。</summary>
    private static string FormatInt(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>以 invariant culture 格式化 nullable integer counter。</summary>
    private static string? FormatNullableInt(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>以 round-trip 格式格式化浮點 latency。</summary>
    private static string FormatDouble(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>格式化 nullable latency milliseconds；不可用時留空。</summary>
    private static string? FormatMilliseconds(TimeSpan? value)
    {
        return value?.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>拒絕 disposed writer 上的寫入。</summary>
    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
