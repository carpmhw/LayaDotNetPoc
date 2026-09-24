using Laya.ConsoleApp.Evaluation;
using Laya.Core.Evaluation;
using System.Text.Json;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class Phase2ReportWriterTests
{
    /// <summary>驗證 report writer 保存 manifest、raw result 與未 rounding metrics。</summary>
    [Fact]
    public void WriteRun_PersistsRecomputableRawArtifacts()
    {
        var run = new LayaPhase2RunResult(
            1,
            new LayaPhase2RunManifest(
                "run-test",
                "english",
                "revision",
                "dataset-hash",
                "development",
                "A",
                "prompt-hash",
                "balanced",
                DateTimeOffset.UnixEpoch,
                "test command"),
            new[]
            {
                new LayaPhase2Prediction(
                    "id-1",
                    "food",
                    "food",
                    "en",
                    LayaPhase2Labels.Categories.ToDictionary(
                        label => label,
                        label => label == "food" ? 0.87654321 : 0.012345679,
                        StringComparer.Ordinal),
                    LayaPhase2Labels.Categories,
                    0.87654321,
                    0.12345678,
                    0.23456789,
                    1.234567,
                    2.345678)
            },
            Array.Empty<LayaPhase2Failure>());
        var root = Directory.CreateTempSubdirectory();

        try
        {
            var output = new Phase2ReportWriter().WriteRun(root.FullName, run);

            Assert.True(File.Exists(output.ManifestPath));
            Assert.True(File.Exists(output.ResultsPath));
            Assert.True(File.Exists(output.MetricsPath));
            Assert.True(File.Exists(output.ArtifactIndexPath));
            Assert.Contains("0.87654321", File.ReadAllText(output.ResultsPath), StringComparison.Ordinal);
            Assert.Contains("0.87654321", File.ReadAllText(output.MetricsPath), StringComparison.Ordinal);
            Assert.Contains("run-test", File.ReadAllText(output.SummaryPath), StringComparison.Ordinal);
            using var results = JsonDocument.Parse(File.ReadAllText(output.ResultsPath));
            Assert.Equal(2, results.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("run-test", results.RootElement.GetProperty("manifest").GetProperty("runId").GetString());
            using var index = JsonDocument.Parse(File.ReadAllText(output.ArtifactIndexPath!));
            Assert.Equal(1, index.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("run-test", index.RootElement.GetProperty("runId").GetString());
            Assert.Equal(new FileInfo(output.ResultsPath).Length,
                index.RootElement.GetProperty("artifacts").GetProperty("results.json").GetProperty("sizeBytes").GetInt64());
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    /// <summary>驗證 partial run 保留全輸入失敗分母並標示 incomplete。</summary>
    [Fact]
    public void WriteRun_WithFailure_PersistsIncompleteFullInputDenominator()
    {
        var run = new LayaPhase2RunResult(
            1,
            new LayaPhase2RunManifest(
                "run-failure",
                "english",
                "revision",
                "dataset-hash",
                "development",
                "A",
                "prompt-hash",
                null,
                DateTimeOffset.UnixEpoch,
                "test command"),
            Array.Empty<LayaPhase2Prediction>(),
            new[] { new LayaPhase2Failure("id-1", "inference", "SyntheticFailure", "en") });
        var root = Directory.CreateTempSubdirectory();

        try
        {
            var output = new Phase2ReportWriter().WriteRun(root.FullName, run);
            using var results = JsonDocument.Parse(File.ReadAllText(output.ResultsPath));
            using var metrics = JsonDocument.Parse(File.ReadAllText(output.MetricsPath));

            Assert.False(results.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(1, results.RootElement.GetProperty("failures").GetArrayLength());
            Assert.Equal(1, metrics.RootElement.GetProperty("metrics").GetProperty("InputCount").GetInt32());
            Assert.Equal(1, metrics.RootElement.GetProperty("metrics").GetProperty("FailureCount").GetInt32());
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }
}
