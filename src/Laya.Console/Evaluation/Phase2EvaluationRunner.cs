using System.Diagnostics;
using Laya.Core.Evaluation;
using Laya.Core.Exceptions;
using Laya.Core.Inference;

namespace Laya.ConsoleApp.Evaluation;

/// <summary>以長生命週期 engine 執行 Phase 2 transaction run 並保存逐筆結果。</summary>
public sealed class Phase2EvaluationRunner
{
    private readonly LayaDecisionEngine _engine;

    /// <summary>建立使用既有長生命週期 decision engine 的 runner。</summary>
    public Phase2EvaluationRunner(LayaDecisionEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>執行一批 transaction，失敗列保留在全輸入分母。</summary>
    public LayaPhase2RunResult Run(
        IReadOnlyList<TransactionRow> rows,
        string profile,
        string modelRevision,
        string datasetHash,
        string split,
        LayaPromptVariant promptVariant = LayaPromptVariant.A,
        string? policyName = null,
        string command = "",
        Phase2DatasetSelection? selection = null,
        string? bundleManifestSha256 = null,
        string? referenceManifestSha256 = null,
        LayaPhase2Policy? fixedPolicy = null,
        string? frozenCandidateManifestSha256 = null,
        string? fixtureHash = null,
        string? frozenCandidatePath = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(split);

        var failures = new List<LayaPhase2Failure>();
        var predictions = new List<LayaPhase2Prediction>();
        foreach (var row in rows)
        {
            try
            {
                var request = LayaTransactionRequestFactory.CreateRequest(
                    new LayaTransactionInput(
                        row.Id,
                        row.Description,
                        row.Amount,
                        row.Currency,
                        row.Direction),
                    promptVariant);
                var stopwatch = Stopwatch.StartNew();
                var result = _engine.Decide(request);
                stopwatch.Stop();
                var category = result.Answers.Single(answer => answer.Name == "category");
                var needsReview = result.Answers.Single(answer => answer.Name == "needs_review");
                var ranked = LayaPhase2Labels.Categories
                    .OrderByDescending(label => category.Probabilities[label])
                    .ThenBy(label => Array.IndexOf(LayaPhase2Labels.Categories.ToArray(), label))
                    .ToArray();
                var secondProbability = ranked.Length > 1
                    ? category.Probabilities[ranked[1]]
                    : category.Probability;
                var noulProbability = needsReview.Probabilities.TryGetValue("true", out var pTrue)
                    ? pTrue
                    : needsReview.Probability;
                predictions.Add(new LayaPhase2Prediction(
                    row.Id,
                    row.ExpectedCategory,
                    category.SelectedOption ?? string.Empty,
                    row.Language,
                    category.Probabilities,
                    ranked,
                    category.Probability,
                    category.Probability - secondProbability,
                    noulProbability,
                    result.InferenceDuration.TotalMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    result.SequenceLength,
                    result.WasTruncated));
            }
            catch (LayaException exception)
            {
                failures.Add(new LayaPhase2Failure(row.Id, exception.Stage, exception.GetType().Name, row.Language));
            }
            catch (Exception exception)
            {
                failures.Add(new LayaPhase2Failure(row.Id, "unexpected", exception.GetType().Name, row.Language));
            }
        }

        var promptHash = LayaPhase2Contracts.PromptHash(promptVariant);
        var createdUtc = DateTimeOffset.UtcNow;
        var manifest = new LayaPhase2RunManifest(
            $"{createdUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}",
            profile,
            modelRevision,
            datasetHash,
            split,
            promptVariant.ToString(),
            promptHash,
            fixedPolicy?.Name ?? policyName,
            createdUtc,
            command,
            selection?.GuidelineHash,
            selection?.ManifestHash,
            selection?.SelectedIdsHash,
            bundleManifestSha256,
            referenceManifestSha256,
            LayaPhase2Contracts.OptionOrderHash(),
            LayaPhase2Contracts.SerializationVersion,
            LayaPhase2Contracts.SerializationHash(),
            fixedPolicy is null ? null : LayaPhase2Contracts.PolicyHash(fixedPolicy),
            frozenCandidateManifestSha256,
            selection?.SelectedIds,
            fixtureHash,
            Environment.GetEnvironmentVariable("LAYA_SOURCE_COMMIT") ?? "unavailable",
            ParseSourceDirty(),
            Environment.GetEnvironmentVariable("LAYA_SOURCE_DIGEST") ?? "unavailable",
            LayaPhase2Contracts.EnvironmentFingerprint(),
            Environment.ProcessorCount,
            NormalizeArtifactPath(frozenCandidatePath));
        return new LayaPhase2RunResult(rows.Count, manifest, predictions, failures);
    }

    /// <summary>將 frozen candidate 路徑保存成 repository-relative canonical path。</summary>
    private static string? NormalizeArtifactPath(string? path)
    {
        return path is null
            ? null
            : Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(path))
                .Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>解析工具注入的 source dirty 狀態，未知時保留 null 而不偽造 clean。</summary>
    private static bool? ParseSourceDirty()
    {
        var value = Environment.GetEnvironmentVariable("LAYA_SOURCE_DIRTY");
        return value is null
            ? null
            : bool.TryParse(value, out var dirty)
                ? dirty
                : throw new LayaConfigurationException("LAYA_SOURCE_DIRTY must be true or false.");
    }
}
