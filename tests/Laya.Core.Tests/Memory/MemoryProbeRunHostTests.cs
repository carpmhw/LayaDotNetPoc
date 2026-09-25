extern alias MemoryProbe;

using System.Text.Json;
using MemoryProbe::Laya.MemoryProbe.Configuration;
using MemoryProbe::Laya.MemoryProbe.Reporting;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "MultilingualModel")]
public sealed class MemoryProbeRunHostTests
{
    /// <summary>驗證單一 tokenizer pilot 串接 T0／warmup／formal checkpoints 與 raw files。</summary>
    [Fact]
    public async Task ExecuteAsync_WritesLifecycleSamplesAndMeasuredLatencyRows()
    {
        var repositoryRoot = GetRepositoryRoot();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-host-{Guid.NewGuid():N}");
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", "tokenizer-only",
            "--requests", "2",
            "--workload", "tokenizer-zh",
            "--run-id", "host-test-001",
            "--output", outputRoot,
            "--idle-seconds", "0"
        ]);
        var profile = MemoryProbe::Laya.Shared.Configuration.LayaProfileResolver.Resolve(
            null,
            "multilingual",
            null,
            repositoryRoot);

        try
        {
            var exitCode = await MemoryProbeRunHost.ExecuteAsync(options, profile, repositoryRoot);
            Assert.Equal(0, exitCode);

            var runDirectory = Path.Combine(outputRoot, "runs", "host-test-001");
            var samples = File.ReadAllLines(Path.Combine(runDirectory, "phase3a-memory-samples.csv"));
            var latency = File.ReadAllLines(Path.Combine(runDirectory, "phase3a-memory-latency.csv"));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "manifest.json")));

            Assert.Contains("t0-before-model-load", string.Join('\n', samples), StringComparison.Ordinal);
            Assert.Contains("t1-after-setup", string.Join('\n', samples), StringComparison.Ordinal);
            Assert.Contains("t2-after-first-operation", string.Join('\n', samples), StringComparison.Ordinal);
            Assert.Contains("warmup-complete", string.Join('\n', samples), StringComparison.Ordinal);
            Assert.Equal(3, latency.Length);
            Assert.Equal("complete", manifest.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, manifest.RootElement.GetProperty("actualCounts").GetProperty("completedRequests").GetInt32());
            Assert.Contains(
                manifest.RootElement.GetProperty("artifacts").EnumerateArray(),
                artifact => artifact.GetProperty("path").GetString() == "summary.json");
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>驗證 scenario 初始化錯誤會留下 T0 raw sample 與 incomplete manifest。</summary>
    [Fact]
    public async Task ExecuteAsync_PreservesIncompleteEvidenceWhenScenarioSetupFails()
    {
        var repositoryRoot = GetRepositoryRoot();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-incomplete-{Guid.NewGuid():N}");
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", "tokenizer-only",
            "--requests", "1",
            "--workload", "unknown-tokenizer-fixture",
            "--run-id", "incomplete-test-001",
            "--output", outputRoot,
            "--idle-seconds", "0"
        ]);
        var profile = MemoryProbe::Laya.Shared.Configuration.LayaProfileResolver.Resolve(
            null,
            "multilingual",
            null,
            repositoryRoot);

        try
        {
            var exitCode = await MemoryProbeRunHost.ExecuteAsync(options, profile, repositoryRoot);
            var runDirectory = Path.Combine(outputRoot, "runs", "incomplete-test-001");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "manifest.json")));
            var samples = File.ReadAllLines(Path.Combine(runDirectory, "phase3a-memory-samples.csv"));

            Assert.Equal(2, exitCode);
            Assert.Equal("incomplete", manifest.RootElement.GetProperty("status").GetString());
            Assert.Equal("probe-error", manifest.RootElement.GetProperty("terminalOutcome").GetString());
            Assert.Contains("t0-before-model-load", string.Join('\n', samples), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>驗證 sample rows 使用穩定 kebab-case scenario 名稱。</summary>
    [Fact]
    public async Task ExecuteAsync_WritesCanonicalFullPipelineScenarioName()
    {
        var repositoryRoot = GetRepositoryRoot();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-scenario-{Guid.NewGuid():N}");
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", "full-pipeline",
            "--requests", "1",
            "--run-id", "scenario-name-test-001",
            "--output", outputRoot,
            "--idle-seconds", "0"
        ]);
        var profile = MemoryProbe::Laya.Shared.Configuration.LayaProfileResolver.Resolve(
            null,
            "multilingual",
            null,
            repositoryRoot);

        try
        {
            Assert.Equal(0, await MemoryProbeRunHost.ExecuteAsync(options, profile, repositoryRoot));
            var samples = File.ReadAllLines(Path.Combine(
                outputRoot,
                "runs",
                "scenario-name-test-001",
                "phase3a-memory-samples.csv"));

            Assert.Equal("full-pipeline", samples[1].Split(',')[2]);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>取得測試執行目錄所屬的 repository 根目錄。</summary>
    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }
}
