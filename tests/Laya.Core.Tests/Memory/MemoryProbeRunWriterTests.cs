extern alias MemoryProbe;

using System.Text.Json;
using MemoryProbe::Laya.MemoryProbe.Reporting;
using MemoryProbe::Laya.MemoryProbe.Scenarios;
using MemoryProbe::Laya.MemoryProbe.Telemetry;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryProbeRunWriterTests
{
    /// <summary>驗證建立 sample CSV 後立即 flush，且不可用 Linux metric 保留原因而非零值。</summary>
    [Fact]
    public void WriteSample_FlushesRawCsvWithUnavailableMetricReason()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-writer-{Guid.NewGuid():N}");
        using var writer = new MemoryProbeRunWriter(outputRoot, "writer-test-001", "full-pipeline");
        var linux = LinuxProcStatusReader.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var sample = new MemoryProbeSample(
            0,
            "writer-test-001",
            "full-pipeline",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(12.5),
            "measurement",
            "idle, \"30\"",
            10,
            10,
            10,
            0,
            0,
            5,
            15,
            TimeSpan.FromMilliseconds(3),
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromMilliseconds(1.5),
            TimeSpan.FromMilliseconds(2.5),
            TimeSpan.FromMilliseconds(2.75),
            TimeSpan.FromMilliseconds(2),
            128,
            false,
            ProcessMemoryCollector.Capture());

        writer.WriteSample(sample, linux);

        var lines = File.ReadAllLines(writer.SamplesPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("vmrss_unavailable_reason", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idle, \"\"30\"\"", lines[1], StringComparison.Ordinal);
        Assert.Contains(linux.VmRss.UnavailableReason!, lines[1], StringComparison.Ordinal);
        Assert.Contains(",12.5,", lines[1], StringComparison.Ordinal);
    }

    /// <summary>驗證負值 managed heap counter 以空值與 unavailable reason 輸出至 raw CSV。</summary>
    [Fact]
    public void WriteSample_RecordsNegativeManagedHeapAsUnavailable()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-managed-heap-writer-{Guid.NewGuid():N}");
        using var writer = new MemoryProbeRunWriter(outputRoot, "managed-heap-test-001", "full-pipeline");
        var managedHeap = MemoryMetricValue.FromReportedBytes(-123, "GC.GetTotalMemory(false)");
        var memory = new ProcessMemorySnapshot(
            100, 200, 300, 1, 1,
            managedHeap,
            0, 0, 0, 0, 0, 0, 0,
            LinuxProcStatusSnapshot.Unavailable("test counters unavailable"));
        var sample = new MemoryProbeSample(
            0,
            "managed-heap-test-001",
            "full-pipeline",
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            "measurement",
            "negative-managed-heap",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            memory);

        writer.WriteSample(sample);

        var lines = File.ReadAllLines(writer.SamplesPath);
        var headers = lines[0].Split(',');
        var values = lines[1].Split(',');
        var managedHeapIndex = Array.IndexOf(headers, "managed_heap_bytes");
        var unavailableReasonIndex = Array.IndexOf(headers, "managed_heap_unavailable_reason");
        Assert.True(managedHeapIndex >= 0);
        Assert.True(unavailableReasonIndex >= 0);
        Assert.Equal(string.Empty, values[managedHeapIndex]);
        Assert.Contains("negative", values[unavailableReasonIndex], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GC.GetTotalMemory(false)", values[unavailableReasonIndex], StringComparison.Ordinal);
    }

    /// <summary>驗證 per-request latency raw CSV 不含 workload 原文且可立即讀取。</summary>
    [Fact]
    public void WriteLatency_FlushesOnlyNumericRequestEvidence()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-latency-{Guid.NewGuid():N}");
        using var writer = new MemoryProbeRunWriter(outputRoot, "latency-test-001", "run-only");

        writer.WriteLatency(
            17,
            new ProbeOperationSample(
                TimeSpan.FromMilliseconds(12.25),
                TimeSpan.FromMilliseconds(4.5),
                integrityFailure: false));

        var lines = File.ReadAllLines(writer.LatencyPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("request_index,end_to_end_latency_ms,inference_latency_ms,integrity_failure", lines[0]);
        Assert.Contains("17,12.25,4.5,false", lines[1]);
        Assert.DoesNotContain("便利商店", lines[1], StringComparison.Ordinal);
    }

    /// <summary>驗證 manifest 以安全替換更新且保存 schema version。</summary>
    [Fact]
    public void WriteManifest_AtomicallyReplacesRunManifest()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-manifest-{Guid.NewGuid():N}");
        using var writer = new MemoryProbeRunWriter(outputRoot, "manifest-test-001", "tokenizer-only");
        writer.WriteManifest(new { schemaVersion = 1, status = "in-progress", runId = "manifest-test-001" });
        writer.WriteManifest(new { schemaVersion = 1, status = "complete", runId = "manifest-test-001" });

        using var manifest = JsonDocument.Parse(File.ReadAllText(writer.ManifestPath));

        Assert.Equal("complete", manifest.RootElement.GetProperty("status").GetString());
        Assert.Equal("manifest-test-001", manifest.RootElement.GetProperty("runId").GetString());
    }

    /// <summary>驗證 run summary 以 machine-readable JSON 保存 count 與 latency percentile。</summary>
    [Fact]
    public void WriteSummary_WritesMachineReadableRunSummary()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-summary-{Guid.NewGuid():N}");
        using var writer = new MemoryProbeRunWriter(outputRoot, "summary-test-001", "full-pipeline");

        writer.WriteSummary(new
        {
            schemaVersion = 1,
            completedRequests = 4,
            latency = new { sampleCount = 4, meanMilliseconds = 2.5, p95Milliseconds = 3.85 }
        });

        using var summary = JsonDocument.Parse(File.ReadAllText(writer.SummaryPath));
        Assert.Equal(4, summary.RootElement.GetProperty("completedRequests").GetInt32());
        Assert.Equal(3.85, summary.RootElement.GetProperty("latency").GetProperty("p95Milliseconds").GetDouble(), precision: 6);
    }

    /// <summary>驗證 run ID 對應的既有目錄永不覆寫。</summary>
    [Fact]
    public void Constructor_RejectsExistingRunDirectory()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-collision-{Guid.NewGuid():N}");
        using (var writer = new MemoryProbeRunWriter(outputRoot, "collision-test-001", "tensor-only"))
        {
            writer.WriteManifest(new { schemaVersion = 1, status = "in-progress" });
        }

        Assert.Throws<IOException>(() => new MemoryProbeRunWriter(outputRoot, "collision-test-001", "tensor-only"));
    }
}
