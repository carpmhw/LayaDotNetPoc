using System.Security.Cryptography;
using System.Text.Json;
using Laya.MemoryProbe.Configuration;
using Laya.Shared.Configuration;

namespace Laya.MemoryProbe.Reporting;

/// <summary>建立與 Phase 3A schema 相容的初始 per-run manifest。</summary>
internal static class MemoryProbeRunManifestBuilder
{
    /// <summary>建立 in-progress manifest 並驗證 fixture 與 verified bundle 身分。</summary>
    public static Dictionary<string, object?> Build(
        MemoryProbeOptions options,
        LayaProfileResolution profile,
        string workloadManifestPath,
        IReadOnlyDictionary<string, object?> sourceIdentity,
        IReadOnlyDictionary<string, object?> environment,
        DateTimeOffset startedUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadManifestPath);
        ArgumentNullException.ThrowIfNull(sourceIdentity);
        ArgumentNullException.ThrowIfNull(environment);

        using var workloadManifest = JsonDocument.Parse(File.ReadAllText(workloadManifestPath));
        var workload = workloadManifest.RootElement.GetProperty("workload");
        var workloadHash = workload.GetProperty("sha256").GetString()
            ?? throw new InvalidDataException("Workload manifest is missing the fixture SHA-256.");
        var workloadPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(workloadManifestPath))!,
            Path.GetFileName(workload.GetProperty("path").GetString()!));
        ValidateHash(workloadPath, workloadHash, "workload fixture");

        var bundleManifestPath = Path.Combine(profile.ModelRoot, "laya-bundle-manifest.json");
        using var bundleManifest = JsonDocument.Parse(File.ReadAllText(bundleManifestPath));
        var bundleRoot = bundleManifest.RootElement;
        var profileName = bundleRoot.GetProperty("profile").GetString();
        var revision = bundleRoot.GetProperty("checkpoint").GetProperty("revision").GetString();
        if (!string.Equals(profileName, profile.Name, StringComparison.Ordinal) ||
            !string.Equals(revision, profile.CheckpointRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Resolved model profile does not match its verified bundle manifest.");
        }

        var tokenizerHash = bundleRoot.GetProperty("files")
            .GetProperty("tokenizer/tokenizer.json")
            .GetProperty("sha256")
            .GetString();
        var bundleManifestHash = HashFile(bundleManifestPath);
        var policyHash = options.PolicyPath is null ? null : HashFile(options.PolicyPath);
        var scenarioName = options.IsLoadOnly ? "load-only" : ToScenarioName(options.Scenario!.Value);
        var campaignId = Environment.GetEnvironmentVariable("LAYA_CAMPAIGN_ID") ?? options.RunId;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["runId"] = options.RunId,
            ["campaignId"] = campaignId,
            ["mode"] = options.Mode == MemoryProbeMode.Pilot ? "pilot" : "formal",
            ["status"] = "in-progress",
            ["scenario"] = scenarioName,
            ["startedUtc"] = startedUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["endedUtc"] = null,
            ["sourceIdentity"] = sourceIdentity,
            ["policySha256"] = policyHash,
            ["workloadSha256"] = workloadHash,
            ["model"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["profile"] = profile.Name,
                ["revision"] = revision,
                ["bundleRoot"] = profile.ModelRoot,
                ["bundleManifestSha256"] = bundleManifestHash,
                ["tokenizerSha256"] = tokenizerHash
            },
            ["environment"] = environment,
            ["requestedConfiguration"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["profile"] = profile.Name,
                ["concurrency"] = options.Concurrency,
                ["cpuArena"] = options.CpuArenaEnabled,
                ["warmupRequests"] = options.WarmupRequests,
                ["sampleEvery"] = options.SampleEvery,
                ["stateProfile"] = options.StateProfile,
                ["questionCount"] = options.QuestionCount,
                ["idleSeconds"] = options.IdleSeconds,
                ["idleOmissionReason"] = options.IdleOmissionReason,
                ["gcCheckpoints"] = options.GcCheckpoints,
                ["gcTreatment"] = options.GcCheckpoints.Count == 0 ? "natural" : "forced-gc-checkpoints",
                ["naturalSamplesExcludeForcedGc"] = true
            },
            ["plannedCounts"] = CreatePlannedCounts(options.WarmupRequests, options.Requests),
            ["actualCounts"] = CreateActualCounts(),
            ["terminalOutcome"] = "in-progress",
            ["failure"] = null,
            ["artifacts"] = Array.Empty<object>()
        };
    }

    /// <summary>取得 manifest 記錄使用的穩定 scenario 名稱。</summary>
    private static string ToScenarioName(MemoryProbeScenario scenario)
    {
        return scenario switch
        {
            MemoryProbeScenario.TokenizerOnly => "tokenizer-only",
            MemoryProbeScenario.SequenceOnly => "sequence-only",
            MemoryProbeScenario.TensorOnly => "tensor-only",
            MemoryProbeScenario.RunOnly => "run-only",
            MemoryProbeScenario.CalibrationOnly => "calibration-only",
            MemoryProbeScenario.FullPipeline => "full-pipeline",
            MemoryProbeScenario.SessionRecreate => "session-recreate",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    /// <summary>建立 manifest 所需的 expected request counter group。</summary>
    private static Dictionary<string, object?> CreatePlannedCounts(int warmupRequests, int requests)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["warmupRequests"] = warmupRequests,
            ["attemptedRequests"] = requests,
            ["completedRequests"] = requests,
            ["errors"] = 0,
            ["integrityFailures"] = 0
        };
    }

    /// <summary>建立零初始值 actual request counter group。</summary>
    private static Dictionary<string, object?> CreateActualCounts()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["warmupRequests"] = 0,
            ["attemptedRequests"] = 0,
            ["completedRequests"] = 0,
            ["errors"] = 0,
            ["integrityFailures"] = 0
        };
    }

    /// <summary>計算指定 artifact 的 SHA-256。</summary>
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>確認 fixture bytes 與 sidecar manifest hash 一致。</summary>
    private static void ValidateHash(string path, string expectedHash, string artifactName)
    {
        var actualHash = HashFile(path);
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The {artifactName} does not match its manifest SHA-256.");
        }
    }
}
