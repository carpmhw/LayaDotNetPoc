using System.Text.Json;
using Laya.ConsoleApp.Evaluation;
using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class Phase2HeldOutReportWriterTests
{
    /// <summary>驗證 held-out writer 只套用 fixed policy，不產生 development grid。</summary>
    [Fact]
    public void WriteRun_WithFixedPolicy_LeavesGridNullAndPersistsCandidateIdentity()
    {
        var directory = Directory.CreateTempSubdirectory("laya-held-out-report-");
        try
        {
            var policy = new LayaPhase2Policy("Balanced", true, 0.95, 0.2, 0.6, 0.1);
            var run = new LayaPhase2RunResult(
                2,
                new LayaPhase2RunManifest(
                    "held-out-run",
                    "multilingual",
                    "checkpoint",
                    "dataset",
                    "held-out",
                    "A",
                    LayaPhase2Contracts.PromptHash(LayaPromptVariant.A),
                    policy.Name,
                    DateTimeOffset.UtcNow,
                    "synthetic",
                    "guideline",
                    "manifest",
                    "selected",
                    "bundle",
                    "reference",
                    LayaPhase2Contracts.OptionOrderHash(),
                    LayaPhase2Contracts.SerializationVersion,
                    LayaPhase2Contracts.SerializationHash(),
                    LayaPhase2Contracts.PolicyHash(policy),
                    "candidate"),
                new[]
                {
                    CreatePrediction("held-1", 0.99, 0.3)
                },
                new[]
                {
                    new LayaPhase2Failure("held-2", "inference", "SyntheticFailure", "en")
                });

            var output = new Phase2ReportWriter().WriteRun(
                directory.FullName,
                run,
                policy,
                "candidate");
            using var manifest = JsonDocument.Parse(File.ReadAllText(output.ManifestPath));
            using var metrics = JsonDocument.Parse(File.ReadAllText(output.MetricsPath));

            Assert.Equal(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("held-out-run", manifest.RootElement.GetProperty("runId").GetString());
            Assert.True(File.Exists(output.ArtifactIndexPath));
            Assert.Equal(JsonValueKind.Null, metrics.RootElement.GetProperty("policyGrid").ValueKind);
            Assert.Equal("candidate", metrics.RootElement.GetProperty("frozenCandidateManifestSha256").GetString());
            Assert.Equal("Balanced", metrics.RootElement.GetProperty("fixedPolicy").GetProperty("Name").GetString());
            Assert.Equal(2, metrics.RootElement.GetProperty("policyDecisions").EnumerateObject()
                .Sum(property => property.Value.GetInt32()));
            Assert.Contains("Fixed policy", File.ReadAllText(output.SummaryPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>建立供 fixed-policy writer 使用的最小成功 prediction。</summary>
    private static LayaPhase2Prediction CreatePrediction(string id, double probability, double margin)
    {
        return new LayaPhase2Prediction(
            id,
            "food",
            "food",
            "en",
            LayaPhase2Labels.Categories.ToDictionary(
                label => label,
                label => label == "food" ? probability : (1 - probability) / 10),
            new[] { "food", "transport" },
            probability,
            margin,
            0.1,
            1,
            1);
    }
}
