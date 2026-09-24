using System.Globalization;
using System.Text.Json;
using Laya.Core.Evaluation;

namespace Laya.ConsoleApp.Evaluation;

/// <summary>保存 Phase 2 raw run 與摘要報告的固定檔案路徑。</summary>
public sealed record Phase2RunOutputPaths(
    string RunDirectory,
    string ManifestPath,
    string ResultsPath,
    string MetricsPath,
    string SummaryPath,
    string? ArtifactIndexPath = null);

/// <summary>從同一份未 rounding run result 寫出可重算的 Phase 2 artifacts。</summary>
public sealed class Phase2ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>保存 manifest、逐筆結果、metrics、calibration 與 markdown 摘要。</summary>
    public Phase2RunOutputPaths WriteRun(
        string reportsRoot,
        LayaPhase2RunResult run,
        LayaPhase2Policy? fixedPolicy = null,
        string? frozenCandidateManifestSha256 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportsRoot);
        ArgumentNullException.ThrowIfNull(run);

        var runDirectory = Path.Combine(reportsRoot, "phase2-runs", run.Manifest.RunId);
        Directory.CreateDirectory(runDirectory);
        var manifestPath = Path.Combine(runDirectory, "manifest.json");
        var resultsPath = Path.Combine(runDirectory, "results.json");
        var metricsPath = Path.Combine(runDirectory, "metrics.json");
        var summaryPath = Path.Combine(runDirectory, "summary.md");
        var artifactIndexPath = Path.Combine(runDirectory, "artifact-index.json");
        var metrics = LayaPhase2Metrics.Calculate(run.Predictions, run.Failures);
        var calibration = LayaCalibrationAnalyzer.Analyze(run.Predictions);
        var noul = LayaNoulAnalyzer.Analyze(run.Predictions);
        var grid = fixedPolicy is null
            ? LayaPolicyEvaluator.EvaluateGrid(run.Predictions, run.InputCount)
            : null;
        var policyDecisions = fixedPolicy is null ? null : CountPolicyDecisions(run, fixedPolicy);

        var manifest = CreateV2Manifest(run.Manifest);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        File.WriteAllText(resultsPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            inputCount = run.InputCount,
            complete = run.Complete,
            manifest,
            predictions = run.Predictions,
            failures = run.Failures
        }, JsonOptions));
        File.WriteAllText(
            metricsPath,
            JsonSerializer.Serialize(
                new
                {
                    metrics,
                    calibration,
                    noul,
                    policyGrid = grid,
                    fixedPolicy,
                    frozenCandidateManifestSha256,
                    policyDecisions
                },
                JsonOptions));
        File.WriteAllText(summaryPath, CreateSummary(run, metrics, calibration, noul, fixedPolicy, frozenCandidateManifestSha256));
        WriteArtifactIndex(artifactIndexPath, run.Manifest.RunId, manifestPath, resultsPath, metricsPath, summaryPath);
        return new Phase2RunOutputPaths(runDirectory, manifestPath, resultsPath, metricsPath, summaryPath, artifactIndexPath);
    }

    /// <summary>將 run manifest 輸出成 canonical camelCase schemaVersion=2。</summary>
    private static object CreateV2Manifest(LayaPhase2RunManifest manifest)
    {
        return new
        {
            schemaVersion = 2,
            runId = manifest.RunId,
            profile = manifest.Profile,
            modelRevision = manifest.ModelRevision,
            datasetHash = manifest.DatasetHash,
            guidelineHash = manifest.GuidelineHash,
            datasetManifestHash = manifest.DatasetManifestHash,
            split = manifest.Split,
            selectedIdsHash = manifest.SelectedIdsHash,
            promptVariant = manifest.PromptVariant,
            promptHash = manifest.PromptHash,
            policyName = manifest.PolicyName,
            policyHash = manifest.PolicyHash,
            frozenCandidateManifestSha256 = manifest.FrozenCandidateManifestSha256,
            optionOrderHash = manifest.OptionOrderHash,
            serializationVersion = manifest.SerializationVersion,
            serializationHash = manifest.SerializationHash,
            bundleManifestSha256 = manifest.BundleManifestSha256,
            referenceManifestSha256 = manifest.ReferenceManifestSha256,
            selectedIds = manifest.SelectedIds,
            fixtureHash = manifest.FixtureHash,
            frozenCandidatePath = manifest.FrozenCandidatePath,
            sourceCommit = manifest.SourceCommit,
            sourceDirty = manifest.SourceDirty,
            sourceDigest = manifest.SourceDigest,
            environmentFingerprint = manifest.EnvironmentFingerprint,
            threadCount = manifest.ThreadCount,
            createdUtc = manifest.CreatedUtc,
            command = manifest.Command
        };
    }

    /// <summary>保存不包含自身 hash 的 artifact index，供 acceptance 重算各輸出。</summary>
    private static void WriteArtifactIndex(
        string path,
        string runId,
        params string[] artifactPaths)
    {
        var artifacts = artifactPaths.ToDictionary(
            artifactPath => Path.GetFileName(artifactPath),
            artifactPath => new
            {
                path = Path.GetFileName(artifactPath),
                sha256 = HashFile(artifactPath),
                sizeBytes = new FileInfo(artifactPath).Length
            },
            StringComparer.Ordinal);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            runId,
            artifacts
        }, JsonOptions));
    }

    /// <summary>以串流方式取得 artifact SHA-256。</summary>
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>只套用已凍結 policy，保存 held-out decision mode 分布而不搜尋 grid。</summary>
    private static IReadOnlyDictionary<string, int> CountPolicyDecisions(
        LayaPhase2RunResult run,
        LayaPhase2Policy policy)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var prediction in run.Predictions)
        {
            var mode = LayaPolicyEvaluator.Classify(prediction, policy).ToString();
            counts[mode] = counts.GetValueOrDefault(mode) + 1;
        }

        foreach (var _ in run.Failures)
        {
            var mode = LayaPolicyEvaluator.ClassifyFailure(policy).ToString();
            counts[mode] = counts.GetValueOrDefault(mode) + 1;
        }

        return counts;
    }

    /// <summary>以未 rounding 計算值建立不含交易原文的 Markdown 摘要。</summary>
    private static string CreateSummary(
        LayaPhase2RunResult run,
        LayaPhase2MetricsReport metrics,
        LayaCalibrationReport calibration,
        LayaNoulReport noul,
        LayaPhase2Policy? fixedPolicy,
        string? frozenCandidateManifestSha256)
    {
        var status = run.Complete ? "complete" : "incomplete";
        return $"# Phase 2 Run `{run.Manifest.RunId}`\n\n" +
            $"- Status: `{status}`\n" +
            $"- Profile: `{run.Manifest.Profile}`\n" +
            $"- Split: `{run.Manifest.Split}`\n" +
            $"- Prompt: `{run.Manifest.PromptVariant}` (`{run.Manifest.PromptHash}`)\n" +
            (fixedPolicy is null
                ? "- Policy: development grid search\n"
                : $"- Fixed policy: `{fixedPolicy.Name}`; frozen candidate `{frozenCandidateManifestSha256}`\n") +
            $"- Input / success / failure: {metrics.InputCount} / {metrics.SuccessCount} / {metrics.FailureCount}\n" +
            $"- Success accuracy: {FormatNullable(metrics.SuccessAccuracy)}\n" +
            $"- Full-input accuracy: {metrics.FullInputAccuracy.ToString("R", CultureInfo.InvariantCulture)}\n" +
            $"- Macro F1: {metrics.MacroF1.ToString("R", CultureInfo.InvariantCulture)}\n" +
            $"- Coverage: {metrics.Coverage.ToString("R", CultureInfo.InvariantCulture)}\n" +
            $"- ECE: {FormatNullable(calibration.Ece)}\n" +
            $"- Noul buckets: {noul.Buckets.Count}\n" +
            "\nFailures are retained in the full-input denominator. Numeric JSON artifacts are the source for recomputation; this summary is descriptive only.\n";
    }

    /// <summary>格式化 nullable metric，無分母時輸出 N/A。</summary>
    private static string FormatNullable(double? value)
    {
        return value is null
            ? "N/A"
            : value.Value.ToString("R", CultureInfo.InvariantCulture);
    }
}
