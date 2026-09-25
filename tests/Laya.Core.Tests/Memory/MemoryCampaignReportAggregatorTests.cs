extern alias MemoryBenchmarks;

using System.Text.Json;
using MemoryBenchmarks::Laya.MemoryBenchmarks.Reporting;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryCampaignReportAggregatorTests
{
    /// <summary>驗證缺少 campaign evidence 時產生完整報告檔案集合且 gate 維持 blocked。</summary>
    [Fact]
    public void AggregateWithoutEvidence_WritesBlockedReportSet()
    {
        var inputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-report-input-{Guid.NewGuid():N}");
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-report-output-{Guid.NewGuid():N}");

        try
        {
            var result = MemoryCampaignReportAggregator.Aggregate(inputRoot, outputRoot, "missing-campaign");

            Assert.Equal("blocked", result.EvidenceStatus);
            Assert.Null(result.GateStatus);
            Assert.Equal("DO NOT PROCEED", result.Recommendation);
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-memory-samples.csv")));
            Assert.Contains("managed_heap_unavailable_reason", File.ReadAllLines(
                Path.Combine(outputRoot, "phase3a-memory-samples.csv"))[0], StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(outputRoot, "baseline-full-pipeline.csv")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "baseline-full-pipeline.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-memory-components.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-lifecycle-audit.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-session-lifecycle.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-ort-arena-comparison.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-shape-memory-matrix.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-memory-concurrency.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-docker-memory.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-reproducibility.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-memory-gate-report.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "phase3a-memory-gate.json")));

            using var gate = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "phase3a-memory-gate.json")));
            Assert.Equal("blocked", gate.RootElement.GetProperty("evidenceStatus").GetString());
            Assert.Null(gate.RootElement.GetProperty("status").GetString());
            Assert.Contains("blocked", File.ReadAllText(Path.Combine(outputRoot, "phase3a-memory-gate-report.md")), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(inputRoot))
            {
                Directory.Delete(inputRoot, recursive: true);
            }

            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>驗證同一 campaign 的 stage source identity 不一致會明確阻擋彙總。</summary>
    [Fact]
    public void AggregateWithMismatchedStageIdentity_ReportsBlocker()
    {
        var inputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-report-identity-{Guid.NewGuid():N}");
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-report-identity-output-{Guid.NewGuid():N}");

        try
        {
            WriteStageManifest(inputRoot, "baseline", "source-commit-a");
            WriteStageManifest(inputRoot, "components", "source-commit-b");

            var result = MemoryCampaignReportAggregator.Aggregate(inputRoot, outputRoot, "identity-campaign");

            Assert.Contains(result.Blockers, blocker => blocker.Contains("identity mismatch", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("blocked", result.EvidenceStatus);
            Assert.Null(result.GateStatus);
        }
        finally
        {
            if (Directory.Exists(inputRoot))
            {
                Directory.Delete(inputRoot, recursive: true);
            }

            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>寫入供彙總器驗證身分一致性的最小 campaign stage manifest。</summary>
    private static void WriteStageManifest(string inputRoot, string stage, string sourceCommit)
    {
        var directory = Path.Combine(inputRoot, "campaigns", "identity-campaign", stage);
        Directory.CreateDirectory(directory);
        var manifest = new Dictionary<string, object?>
        {
            ["campaignId"] = "identity-campaign",
            ["stage"] = stage,
            ["mode"] = "pilot",
            ["evidenceStatus"] = "complete",
            ["requiredRunIds"] = Array.Empty<string>(),
            ["sourceIdentity"] = new Dictionary<string, object?>
            {
                ["commit"] = sourceCommit,
                ["dirty"] = false,
                ["dirtyIdentity"] = null
            },
            ["policy"] = null,
            ["workload"] = new Dictionary<string, object?> { ["sha256"] = "workload-hash" },
            ["model"] = new Dictionary<string, object?>
            {
                ["revision"] = "model-revision",
                ["bundleManifestSha256"] = "bundle-hash",
                ["tokenizerSha256"] = "tokenizer-hash"
            },
            ["environment"] = new Dictionary<string, object?>
            {
                ["dotnetRuntime"] = "8.0.31",
                ["onnxRuntime"] = "1.22.0"
            }
        };
        File.WriteAllText(Path.Combine(directory, "campaign.json"), JsonSerializer.Serialize(manifest));
    }
}
