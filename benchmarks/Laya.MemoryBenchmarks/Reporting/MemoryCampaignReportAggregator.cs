using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Laya.MemoryBenchmarks.Analysis;

namespace Laya.MemoryBenchmarks.Reporting;

/// <summary>保存 report aggregation 的 evidence／gate 結果。</summary>
internal sealed class MemoryCampaignReportResult
{
    /// <summary>建立 aggregaton result descriptor。</summary>
    public MemoryCampaignReportResult(
        string evidenceStatus,
        string? gateStatus,
        MemoryDiagnosticClassification? classification,
        string recommendation,
        IReadOnlyList<string> blockers)
    {
        EvidenceStatus = evidenceStatus;
        GateStatus = gateStatus;
        Classification = classification;
        Recommendation = recommendation;
        Blockers = blockers;
    }

    /// <summary>取得 campaign evidence complete／blocked。</summary>
    public string EvidenceStatus { get; }

    /// <summary>取得 PASS／PARTIAL／FAIL；blocked 時為 null。</summary>
    public string? GateStatus { get; }

    /// <summary>取得 memory diagnostic classification；證據不足為 null。</summary>
    public MemoryDiagnosticClassification? Classification { get; }

    /// <summary>取得 Phase 3B recommendation。</summary>
    public string Recommendation { get; }

    /// <summary>取得缺失或未驗證 evidence 理由。</summary>
    public IReadOnlyList<string> Blockers { get; }
}

/// <summary>由 campaign manifests 與 raw run artifacts 重算 Phase 3A reports 與 Gate JSON。</summary>
internal static class MemoryCampaignReportAggregator
{
    private static readonly string[] RequiredStages =
    {
        "baseline", "components", "lifecycle", "arena", "shape", "concurrency", "reproduction"
    };

    private static readonly string[] SampleHeader =
    {
        "run_id", "sample_index", "scenario", "timestamp_utc", "elapsed_ms", "phase", "reason", "request_count",
        "attempted_requests", "completed_requests", "errors", "integrity_failures", "warmup_requests",
        "total_operation_count", "working_set_bytes", "private_memory_bytes", "virtual_memory_bytes",
        "handle_count", "threads", "managed_heap_bytes", "managed_heap_unavailable_reason", "heap_size_bytes", "fragmented_bytes", "gc_index",
        "gen0", "gen1", "gen2", "total_allocated_bytes", "vmrss_bytes", "vmrss_unavailable_reason",
        "vmsize_bytes", "vmsize_unavailable_reason", "vmdata_bytes", "vmdata_unavailable_reason",
        "rss_anon_bytes", "rss_anon_unavailable_reason", "rss_file_bytes", "rss_file_unavailable_reason",
        "rss_shmem_bytes", "rss_shmem_unavailable_reason", "last_latency_ms", "mean_latency_ms", "p50_latency_ms",
        "p95_latency_ms", "p99_latency_ms", "last_inference_ms", "sequence_length", "was_truncated"
    };

    /// <summary>彙總指定 campaign run artifacts，輸出 supporting reports 與 blocked/pass Gate evidence。</summary>
    public static MemoryCampaignReportResult Aggregate(
        string evidenceRoot,
        string outputRoot,
        string campaignId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        Directory.CreateDirectory(outputRoot);

        var blockers = new List<string>();
        var stageManifests = ReadStageManifests(evidenceRoot, campaignId, blockers);
        ValidateCampaignStageConsistency(stageManifests, blockers);
        var runs = ReadRunManifests(evidenceRoot, stageManifests, blockers);
        WriteCombinedSamples(Path.Combine(outputRoot, "phase3a-memory-samples.csv"), runs);
        var baseline = runs.FirstOrDefault(run => run.Scenario == "full-pipeline" && run.Stage == "baseline");
        WriteBaselineCsv(Path.Combine(outputRoot, "baseline-full-pipeline.csv"), baseline);
        var baselineSlopes = AnalyzeBaseline(baseline, blockers);

        WriteBaselineReport(Path.Combine(outputRoot, "baseline-full-pipeline.md"), campaignId, baseline, baselineSlopes);
        WriteStageReport(Path.Combine(outputRoot, "phase3a-memory-components.md"), "Phase 3A 記憶體元件拆解", "components", runs, stageManifests);
        WriteLifecycleAuditReport(Path.Combine(outputRoot, "phase3a-lifecycle-audit.md"), evidenceRoot);
        WriteStageReport(Path.Combine(outputRoot, "phase3a-session-lifecycle.md"), "Phase 3A Session 生命週期", "lifecycle", runs, stageManifests);
        WriteStageReport(Path.Combine(outputRoot, "phase3a-ort-arena-comparison.md"), "Phase 3A CPU Arena 比較", "arena", runs, stageManifests);
        WriteStageReport(Path.Combine(outputRoot, "phase3a-shape-memory-matrix.md"), "Phase 3A Shape 記憶體矩陣", "shape", runs, stageManifests);
        WriteStageReport(Path.Combine(outputRoot, "phase3a-memory-concurrency.md"), "Phase 3A 記憶體並行矩陣", "concurrency", runs, stageManifests);
        WriteDockerReport(Path.Combine(outputRoot, "phase3a-docker-memory.md"), evidenceRoot, campaignId, blockers);
        WriteStageReport(Path.Combine(outputRoot, "phase3a-reproducibility.md"), "Phase 3A 重現性", "reproduction", runs, stageManifests);
        AddRequiredSupportingEvidenceBlockers(evidenceRoot, campaignId, blockers);

        var gateEvidence = CreateGateEvidence(evidenceRoot, campaignId, stageManifests, runs, baselineSlopes, blockers);
        var decision = MemoryGateEvaluator.Evaluate(gateEvidence);
        var gateJson = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["campaignId"] = campaignId,
            ["evidenceStatus"] = decision.EvidenceStatus == MemoryEvidenceStatus.Complete ? "complete" : "blocked",
            ["status"] = decision.Status?.ToString().ToUpperInvariant(),
            ["classification"] = FormatClassification(decision.Classification),
            ["recommendation"] = decision.Recommendation,
            ["blockers"] = decision.Reasons.Concat(blockers).Distinct(StringComparer.Ordinal).ToArray(),
            ["runIds"] = runs.Select(run => run.RunId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            ["slopeSegments"] = baselineSlopes.ToDictionary(
                item => item.Key,
                item => (object?)item.Value.ToDictionary(
                    segment => segment.Key,
                    segment => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["complete"] = segment.Value.IsComplete,
                        ["slopeBytesPer100Requests"] = segment.Value.SlopeBytesPer100Requests,
                        ["endpointDeltaBytes"] = segment.Value.EndpointDeltaBytes,
                        ["rSquared"] = segment.Value.RSquared,
                        ["sampleCount"] = segment.Value.SampleCount,
                        ["unavailableReason"] = segment.Value.UnavailableReason
                    }))
        };
        File.WriteAllText(Path.Combine(outputRoot, "phase3a-memory-gate.json"), JsonSerializer.Serialize(gateJson, new JsonSerializerOptions { WriteIndented = true }));
        WriteGateReport(Path.Combine(outputRoot, "phase3a-memory-gate-report.md"), gateJson);
        WriteAggregateIndex(outputRoot, campaignId);

        return new MemoryCampaignReportResult(
            (string)gateJson["evidenceStatus"]!,
            gateJson["status"] as string,
            decision.Classification,
            decision.Recommendation,
            ((IEnumerable<string>)gateJson["blockers"]!).ToArray());
    }

    /// <summary>載入 campaign stage manifests，並記錄缺失或 blocked stages。</summary>
    private static IReadOnlyList<CampaignStageEvidence> ReadStageManifests(
        string evidenceRoot,
        string campaignId,
        ICollection<string> blockers)
    {
        var campaignRoot = Path.Combine(evidenceRoot, "campaigns", campaignId);
        if (!Directory.Exists(campaignRoot))
        {
            blockers.Add($"Campaign directory is missing: {campaignRoot}");
            return Array.Empty<CampaignStageEvidence>();
        }

        var stages = new List<CampaignStageEvidence>();
        foreach (var path in Directory.GetFiles(campaignRoot, "campaign.json", SearchOption.AllDirectories))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                var stage = root.GetProperty("stage").GetString() ?? string.Empty;
                var status = root.GetProperty("evidenceStatus").GetString() ?? "blocked";
                var runIds = root.GetProperty("requiredRunIds").EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray();
                stages.Add(new CampaignStageEvidence(stage, status, runIds, path));
                if (status != "complete")
                {
                    blockers.Add($"Campaign stage '{stage}' is {status}.");
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                blockers.Add($"Campaign manifest '{path}' is invalid: {exception.Message}");
            }
        }

        foreach (var requiredStage in RequiredStages)
        {
            if (!stages.Any(stage => string.Equals(stage.Stage, requiredStage, StringComparison.Ordinal)))
            {
                blockers.Add($"Required campaign stage '{requiredStage}' is missing.");
            }
        }

        return stages;
    }

    /// <summary>確認同一 campaign 的 stages 共用一致 source、policy、workload、bundle 與 runtime 身分。</summary>
    private static void ValidateCampaignStageConsistency(
        IReadOnlyList<CampaignStageEvidence> stages,
        ICollection<string> blockers)
    {
        if (stages.Count < 2)
        {
            return;
        }

        CampaignStageIdentity firstIdentity;
        try
        {
            using var firstDocument = JsonDocument.Parse(File.ReadAllText(stages[0].ManifestPath));
            firstIdentity = ReadCampaignStageIdentity(firstDocument.RootElement);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            blockers.Add($"Campaign stage '{stages[0].Stage}' identity cannot be read: {exception.Message}");
            return;
        }

        foreach (var stage in stages.Skip(1))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(stage.ManifestPath));
                if (ReadCampaignStageIdentity(document.RootElement) != firstIdentity)
                {
                    blockers.Add($"Campaign stage '{stage.Stage}' has a source, policy, workload, bundle or runtime identity mismatch.");
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                blockers.Add($"Campaign stage '{stage.Stage}' identity cannot be compared: {exception.Message}");
            }
        }
    }

    /// <summary>從 campaign manifest 擷取跨 stage 必須固定的實驗身分欄位。</summary>
    private static CampaignStageIdentity ReadCampaignStageIdentity(JsonElement manifest)
    {
        var source = manifest.GetProperty("sourceIdentity");
        var policy = manifest.GetProperty("policy");
        var workload = manifest.GetProperty("workload");
        var model = manifest.GetProperty("model");
        var environment = manifest.GetProperty("environment");
        return new CampaignStageIdentity(
            ReadRequiredString(manifest, "campaignId"),
            ReadRequiredString(manifest, "mode"),
            ReadRequiredString(source, "commit"),
            source.GetProperty("dirty").GetBoolean(),
            ReadOptionalString(source, "dirtyIdentity"),
            policy.ValueKind == JsonValueKind.Null ? null : ReadRequiredString(policy, "sha256"),
            ReadRequiredString(workload, "sha256"),
            ReadRequiredString(model, "revision"),
            ReadRequiredString(model, "bundleManifestSha256"),
            ReadRequiredString(model, "tokenizerSha256"),
            ReadRequiredString(environment, "dotnetRuntime"),
            ReadRequiredString(environment, "onnxRuntime"));
    }

    /// <summary>載入各 child run manifest 與已完成 raw sample paths。</summary>
    private static IReadOnlyList<RunEvidence> ReadRunManifests(
        string evidenceRoot,
        IReadOnlyList<CampaignStageEvidence> stages,
        ICollection<string> blockers)
    {
        var runs = new List<RunEvidence>();
        foreach (var stage in stages)
        {
            foreach (var runId in stage.RunIds)
            {
                var runDirectory = Path.Combine(evidenceRoot, "runs", runId);
                var manifestPath = Path.Combine(runDirectory, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    blockers.Add($"Run '{runId}' manifest is missing.");
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    var root = document.RootElement.Clone();
                    ValidateRunIdentity(root, stage, runId, blockers);
                    ValidateRunArtifacts(root, runDirectory, runId, blockers);
                    var status = root.GetProperty("status").GetString() ?? "incomplete";
                    if (status != "complete")
                    {
                        blockers.Add($"Run '{runId}' is {status}.");
                    }

                    var samplesPath = Path.Combine(runDirectory, "phase3a-memory-samples.csv");
                    var latencyPath = Path.Combine(runDirectory, "phase3a-memory-latency.csv");
                    runs.Add(new RunEvidence(
                        runId,
                        stage.Stage,
                        root.GetProperty("scenario").GetString() ?? string.Empty,
                        root,
                        runDirectory,
                        File.Exists(samplesPath) ? samplesPath : null,
                        File.Exists(latencyPath) ? latencyPath : null));
                    if (!File.Exists(samplesPath) || !File.Exists(latencyPath))
                    {
                        blockers.Add($"Run '{runId}' is missing samples or raw latency.");
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
                {
                    blockers.Add($"Run '{runId}' manifest is invalid: {exception.Message}");
                }
            }
        }

        return runs;
    }

    /// <summary>比較 child run source／policy／workload／model 身分與 stage campaign manifest。</summary>
    private static void ValidateRunIdentity(
        JsonElement runManifest,
        CampaignStageEvidence stage,
        string runId,
        ICollection<string> blockers)
    {
        try
        {
            using var campaignDocument = JsonDocument.Parse(File.ReadAllText(stage.ManifestPath));
            var campaign = campaignDocument.RootElement;
            var campaignSource = campaign.GetProperty("sourceIdentity");
            var runSource = runManifest.GetProperty("sourceIdentity");
            var campaignPolicy = campaign.GetProperty("policy");
            var expectedPolicy = campaignPolicy.ValueKind == JsonValueKind.Null
                ? null
                : campaignPolicy.GetProperty("sha256").GetString();
            var runPolicy = ReadOptionalString(runManifest, "policySha256");
            var campaignWorkload = campaign.GetProperty("workload");
            var runModel = runManifest.GetProperty("model");
            var campaignModel = campaign.GetProperty("model");
            if (!string.Equals(ReadRequiredString(runManifest, "runId"), runId, StringComparison.Ordinal) ||
                !string.Equals(ReadRequiredString(runManifest, "campaignId"), ReadRequiredString(campaign, "campaignId"), StringComparison.Ordinal) ||
                !string.Equals(ReadRequiredString(runManifest, "mode"), ReadRequiredString(campaign, "mode"), StringComparison.Ordinal) ||
                !string.Equals(ReadRequiredString(runSource, "commit"), ReadRequiredString(campaignSource, "commit"), StringComparison.Ordinal) ||
                runSource.GetProperty("dirty").GetBoolean() != campaignSource.GetProperty("dirty").GetBoolean() ||
                ReadOptionalString(runSource, "dirtyIdentity") != ReadOptionalString(campaignSource, "dirtyIdentity") ||
                runPolicy != expectedPolicy ||
                !string.Equals(ReadRequiredString(runManifest, "workloadSha256"), ReadRequiredString(campaignWorkload, "sha256"), StringComparison.Ordinal) ||
                !string.Equals(ReadRequiredString(runModel, "revision"), ReadRequiredString(campaignModel, "revision"), StringComparison.Ordinal) ||
                !string.Equals(ReadRequiredString(runModel, "bundleManifestSha256"), ReadRequiredString(campaignModel, "bundleManifestSha256"), StringComparison.Ordinal) ||
                !string.Equals(ReadRequiredString(runModel, "tokenizerSha256"), ReadRequiredString(campaignModel, "tokenizerSha256"), StringComparison.Ordinal))
            {
                blockers.Add($"Run '{runId}' source, policy, workload or model identity differs from its campaign.");
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            blockers.Add($"Run '{runId}' campaign identity could not be verified: {exception.Message}");
        }
    }

    /// <summary>驗證每個 raw run artifact 位於 run directory 且 matches manifest SHA-256／size。</summary>
    private static void ValidateRunArtifacts(
        JsonElement runManifest,
        string runDirectory,
        string runId,
        ICollection<string> blockers)
    {
        try
        {
            var artifacts = runManifest.GetProperty("artifacts").EnumerateArray().ToArray();
            if (artifacts.Length == 0)
            {
                blockers.Add($"Run '{runId}' has no raw artifacts in its manifest.");
                return;
            }

            var fullRoot = Path.GetFullPath(runDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var artifact in artifacts)
            {
                var relativePath = ReadRequiredString(artifact, "path");
                var fullPath = Path.GetFullPath(Path.Combine(runDirectory, relativePath));
                if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal) || !File.Exists(fullPath))
                {
                    blockers.Add($"Run '{runId}' artifact '{relativePath}' is missing or outside its run directory.");
                    continue;
                }

                if (new FileInfo(fullPath).Length != artifact.GetProperty("sizeBytes").GetInt64() ||
                    !string.Equals(HashFile(fullPath), ReadRequiredString(artifact, "sha256"), StringComparison.Ordinal))
                {
                    blockers.Add($"Run '{runId}' artifact '{relativePath}' hash or size differs from its manifest.");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            blockers.Add($"Run '{runId}' artifact index is invalid: {exception.Message}");
        }
    }

    /// <summary>合併所有 run sample records 並保留原始 run／sample identities。</summary>
    private static void WriteCombinedSamples(string outputPath, IReadOnlyList<RunEvidence> runs)
    {
        using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(false));
        WriteCsvRow(writer, SampleHeader);
        foreach (var run in runs.Where(item => item.SamplesPath is not null))
        {
            using var reader = File.OpenText(run.SamplesPath!);
            var records = CsvRecordReader.ReadRecords(reader).GetEnumerator();
            if (!records.MoveNext())
            {
                continue;
            }

            var sourceHeader = records.Current;
            var indices = SampleHeader.Skip(1).Select(name => FindHeaderIndex(sourceHeader, name)).ToArray();
            while (records.MoveNext())
            {
                var source = records.Current;
                var row = new string?[SampleHeader.Length];
                row[0] = run.RunId;
                for (var index = 0; index < indices.Length; index++)
                {
                    row[index + 1] = indices[index] >= 0 && indices[index] < source.Count ? source[indices[index]] : string.Empty;
                }

                WriteCsvRow(writer, row);
            }
        }
    }

    /// <summary>複製 baseline samples CSV；baseline 缺失時只寫 header。</summary>
    private static void WriteBaselineCsv(string outputPath, RunEvidence? baseline)
    {
        if (baseline?.SamplesPath is not null)
        {
            File.Copy(baseline.SamplesPath, outputPath, overwrite: true);
            return;
        }

        using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(false));
        WriteCsvRow(writer, SampleHeader.Skip(1));
    }

    /// <summary>計算 baseline 四個固定 request segments 的 required process metrics。</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, MemorySlopeSegmentResult>> AnalyzeBaseline(
        RunEvidence? baseline,
        ICollection<string> blockers)
    {
        var metrics = new[] { "working_set_bytes", "private_memory_bytes", "vmrss_bytes", "managed_heap_bytes" };
        var output = new Dictionary<string, IReadOnlyDictionary<string, MemorySlopeSegmentResult>>(StringComparer.Ordinal);
        if (baseline?.SamplesPath is null)
        {
            blockers.Add("Baseline full-pipeline samples are missing; segmented slopes are unavailable.");
            return output;
        }

        var records = ReadSampleRows(baseline.SamplesPath).ToArray();
        foreach (var metric in metrics)
        {
            var points = records
                .Select(row =>
                {
                    var phase = row.GetValueOrDefault("phase", string.Empty);
                    if (!string.Equals(phase, "measurement", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    var count = ParseInt(row, "request_count");
                    var value = ParseNullableLong(row, metric);
                    var forcedGcTreatment = baseline.Manifest.GetProperty("requestedConfiguration")
                        .GetProperty("gcCheckpoints").GetArrayLength() > 0;
                    return count.HasValue
                        ? new MemorySamplePoint(
                            count.Value,
                            value,
                            ParseInt(row, "sample_index") ?? 0,
                            forcedGcTreatment)
                        : null;
                })
                .Where(point => point is not null)
                .Select(point => point!)
                .ToArray();
            output[metric] = MemorySlopeAnalyzer.AnalyzeRequiredSegments(points);
        }

        if (output.Values.SelectMany(segments => segments.Values).Any(segment => !segment.IsComplete))
        {
            blockers.Add("One or more baseline slope segments lack required endpoints or metric values.");
        }

        return output;
    }

    /// <summary>由 frozen policy、baseline slopes、replicates 與 supporting signoffs 建立 Gate facts。</summary>
    private static MemoryGateEvidence CreateGateEvidence(
        string evidenceRoot,
        string campaignId,
        IReadOnlyList<CampaignStageEvidence> stages,
        IReadOnlyList<RunEvidence> runs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, MemorySlopeSegmentResult>> slopes,
        ICollection<string> blockers)
    {
        _ = campaignId;
        var baseline = runs.FirstOrDefault(run => run.Stage == "baseline" && run.Scenario == "full-pipeline");
        var privateMemoryLate = GetLateSegment(slopes, "private_memory_bytes");
        var rssLate = GetLateSegment(slopes, "vmrss_bytes");
        var managedLate = GetLateSegment(slopes, "managed_heap_bytes");
        var policyPath = GetPolicyPath(stages);
        FrozenMemoryPolicy? policy = null;
        if (policyPath is null || !File.Exists(policyPath))
        {
            blockers.Add("A frozen memory-policy.json is missing from the campaign manifests.");
        }
        else
        {
            try
            {
                policy = MemoryPolicyLoader.Load(policyPath);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                blockers.Add($"Frozen memory policy cannot be validated: {exception.Message}");
            }
        }

        var lifecyclePath = Path.Combine(evidenceRoot, "phase3a-lifecycle-audit.json");
        var dockerPath = Path.Combine(evidenceRoot, "docker", campaignId, "phase3a-docker-memory.json");
        var reproducibilityPath = Path.Combine(evidenceRoot, "phase3a-reproducibility.json");
        using var lifecycle = ReadOptionalJson(lifecyclePath);
        using var docker = ReadOptionalJson(dockerPath);
        using var reproducibility = ReadOptionalJson(reproducibilityPath);
        var errors = ReadRunCounter(baseline, "errors");
        var integrityFailures = ReadRunCounter(baseline, "integrityFailures");
        var requests = ReadRunCounter(baseline, "attemptedRequests");
        var privatePlateau = policy is not null && IsPlateau(privateMemoryLate, policy.PrivateMemory);
        var rssPlateau = policy is not null && IsPlateau(rssLate, policy.Rss);
        var managedStable = policy is not null && IsPlateau(managedLate, policy.ManagedHeap);
        var persistentGrowth = policy is not null &&
            IsPersistentGrowth(privateMemoryLate, policy.PrivateMemory, policy.NearLinearR2Minimum) &&
            IsPersistentGrowth(rssLate, policy.Rss, policy.NearLinearR2Minimum);

        var lifecycleProperties = lifecycle?.RootElement;
        var dockerProperties = docker?.RootElement;
        var reproductionProperties = reproducibility?.RootElement;
        return new MemoryGateEvidence
        {
            EvidenceComplete = blockers.Count == 0 && lifecycle is not null && docker is not null && reproducibility is not null,
            FullPipelineRequests = requests,
            CrashCount = ReadOptionalInt(dockerProperties, "crashCount"),
            OomCount = ReadOptionalInt(dockerProperties, "oomCount"),
            Errors = errors,
            IntegrityFailures = integrityFailures,
            PrivateMemoryPlateau = privatePlateau,
            RssPlateau = rssPlateau,
            ManagedHeapStable = managedStable,
            TargetDockerStable = ReadOptionalBool(dockerProperties, "targetStable"),
            ReplicatesConsistent = ReadOptionalBool(reproductionProperties, "consistent"),
            TargetDockerRepeatedOom = ReadOptionalBool(dockerProperties, "targetRepeatedOom"),
            PersistentNearLinearGrowth = persistentGrowth,
            NoPlateauAt5000 = requests >= 5000 && (!privatePlateau || !rssPlateau),
            SessionRecreateUnboundedGrowth = ReadOptionalBool(lifecycleProperties, "sessionRecreateUnboundedGrowth"),
            LeakReproduced = ReadOptionalBool(lifecycleProperties, "leakReproduced"),
            RetentionSourceIdentified = ReadOptionalBool(lifecycleProperties, "retentionSourceIdentified"),
            LifecycleAuditConfirmsUndisposedResource = ReadOptionalBool(lifecycleProperties, "lifecycleAuditConfirmsUndisposedResource"),
            NativeRetentionObserved = ReadOptionalBool(lifecycleProperties, "nativeRetentionObserved"),
            AllocatorComparisonSupportsPlateau = ReadOptionalBool(dockerProperties, "allocatorComparisonSupportsPlateau"),
            BoundedNativeRetention = ReadOptionalBool(lifecycleProperties, "boundedNativeRetention"),
            NativeRetentionExplained = ReadOptionalBool(lifecycleProperties, "nativeRetentionExplained"),
            ArenaTradeoffExplained = ReadOptionalBool(dockerProperties, "arenaTradeoffExplained"),
            DeploymentLimitDocumented = ReadOptionalBool(dockerProperties, "deploymentLimitDocumented"),
            MonitoringConditionsDocumented = ReadOptionalBool(dockerProperties, "monitoringConditionsDocumented"),
            WorkingSetPeakAboveDiagnosticBoundary = false
        };
    }

    /// <summary>取得 PrivateMemory／RSS／managed heap 的 required late segment。</summary>
    private static MemorySlopeSegmentResult? GetLateSegment(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, MemorySlopeSegmentResult>> slopes,
        string metric)
    {
        return slopes.TryGetValue(metric, out var segments) && segments.TryGetValue("2500-5000", out var result)
            ? result
            : null;
    }

    /// <summary>依 frozen late slope 與 growth budgets 判斷 bounded plateau。</summary>
    private static bool IsPlateau(MemorySlopeSegmentResult? result, MemorySlopeBudget budget)
    {
        return result is { IsComplete: true, SlopeBytesPer100Requests: not null, EndpointDeltaBytes: not null } &&
            Math.Abs(result.SlopeBytesPer100Requests.Value) <= budget.MaximumLateSlopeBytesPer100Requests &&
            Math.Abs(result.EndpointDeltaBytes.Value) <= budget.LateGrowthBudgetBytes;
    }

    /// <summary>依 slope threshold 與 R-squared 判斷 persistent near-linear growth。</summary>
    private static bool IsPersistentGrowth(
        MemorySlopeSegmentResult? result,
        MemorySlopeBudget budget,
        double minimumR2)
    {
        return result is { IsComplete: true, SlopeBytesPer100Requests: not null, EndpointDeltaBytes: not null, RSquared: not null } &&
            result.SlopeBytesPer100Requests.Value > budget.MaximumLateSlopeBytesPer100Requests &&
            result.EndpointDeltaBytes.Value > budget.LateGrowthBudgetBytes &&
            result.RSquared.Value >= minimumR2;
    }

    /// <summary>從 campaign stage manifests 取得 frozen policy absolute path。</summary>
    private static string? GetPolicyPath(IReadOnlyList<CampaignStageEvidence> stages)
    {
        foreach (var stage in stages)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(stage.ManifestPath));
            if (document.RootElement.TryGetProperty("policy", out var policy) &&
                policy.ValueKind == JsonValueKind.Object &&
                policy.TryGetProperty("path", out var path))
            {
                return path.GetString();
            }
        }

        return null;
    }

    /// <summary>讀取 optional supporting JSON；無效 evidence 作為 blocker 交由 caller 明確處理。</summary>
    private static JsonDocument? ReadOptionalJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>由 run manifest 取得 actual counter，baseline 缺失時回傳零供 blocked evidence。</summary>
    private static int ReadRunCounter(RunEvidence? run, string counterName)
    {
        if (run is null ||
            !run.Manifest.TryGetProperty("actualCounts", out var counts) ||
            !counts.TryGetProperty(counterName, out var value))
        {
            return 0;
        }

        return value.GetInt32();
    }

    /// <summary>從 optional signoff JSON 讀取 non-negative int counter。</summary>
    private static int ReadOptionalInt(JsonElement? element, string propertyName)
    {
        return element.HasValue && element.Value.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    /// <summary>從 optional signoff JSON 讀取 bool disposition。</summary>
    private static bool ReadOptionalBool(JsonElement? element, string propertyName)
    {
        return element.HasValue && element.Value.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.True;
    }

    /// <summary>輸出 baseline measured count、checkpoint memory 與可重算 segmented OLS table。</summary>
    private static void WriteBaselineReport(
        string path,
        string campaignId,
        RunEvidence? baseline,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, MemorySlopeSegmentResult>> slopes)
    {
        var lines = new List<string>
        {
            "# Phase 3A Full-Pipeline 記憶體基線",
            string.Empty,
            $"Campaign ID：`{campaignId}`",
            $"狀態：**{(baseline is null ? "BLOCKED：缺少基線 run" : "已取得 raw run；仍須完成 policy／Gate 驗證")}**",
            "",
            baseline is null ? "找不到 baseline run manifest；不推測或填入任何量測值。" : $"來源 run：`{baseline.RunId}`",
            "",
            "## 分段 OLS slope（bytes／100 requests）",
            "",
            "| Metric | Request 區間 | Slope | 端點差值 | R² | 樣本數 | 狀態 |",
            "|---|---:|---:|---:|---:|---:|---|"
        };
        foreach (var metric in slopes)
        {
            foreach (var segment in metric.Value)
            {
                var result = segment.Value;
                lines.Add($"| {metric.Key} | {segment.Key} | {Format(result.SlopeBytesPer100Requests)} | {Format(result.EndpointDeltaBytes)} | {Format(result.RSquared)} | {result.SampleCount} | {(result.IsComplete ? "complete" : result.UnavailableReason)} |");
            }
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    /// <summary>輸出指定 stage 的 run ID、scenario、planned／actual counts 與 evidence status。</summary>
    private static void WriteStageReport(
        string path,
        string title,
        string stageName,
        IReadOnlyList<RunEvidence> runs,
        IReadOnlyList<CampaignStageEvidence> stages)
    {
        var stageRuns = runs.Where(run => run.Stage == stageName).ToArray();
        var stage = stages.FirstOrDefault(item => item.Stage == stageName);
        var lines = new List<string>
        {
            $"# {title}",
            string.Empty,
            $"狀態：**{(stage?.Status == "complete" ? "已取得 raw runs；仍須檢閱" : "BLOCKED：缺少或未完成 stage")}**",
            string.Empty,
            "| Run ID | Scenario | 規劃數 | 嘗試數 | 完成數 | 錯誤 | Integrity failures |",
            "|---|---|---:|---:|---:|---:|---:|"
        };
        foreach (var run in stageRuns)
        {
            var planned = run.Manifest.GetProperty("plannedCounts");
            var actual = run.Manifest.GetProperty("actualCounts");
            lines.Add($"| `{run.RunId}` | {run.Scenario} | {planned.GetProperty("attemptedRequests").GetInt32()} | {actual.GetProperty("attemptedRequests").GetInt32()} | {actual.GetProperty("completedRequests").GetInt32()} | {actual.GetProperty("errors").GetInt32()} | {actual.GetProperty("integrityFailures").GetInt32()} |");
        }

        if (stageRuns.Length == 0)
        {
            lines.Add("| — | — | — | — | — | — | — |");
            lines.Add(string.Empty);
            lines.Add($"找不到 campaign stage `{stageName}`；不推測或填入任何結果。");
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    /// <summary>輸出 owner/lifecycle audit 尚待人工 disposition 的明確報告。</summary>
    private static void WriteLifecycleAuditReport(string path, string evidenceRoot)
    {
        var signoffPath = Path.Combine(evidenceRoot, "phase3a-lifecycle-audit.json");
        if (File.Exists(signoffPath))
        {
            File.Copy(signoffPath, path, overwrite: true);
            return;
        }

        var lines = new[]
        {
            "# Phase 3A 生命週期稽核",
            string.Empty,
            "狀態：**BLOCKED — 尚需完成 source-level owner disposition**",
            string.Empty,
            "| Owner | Source | 待完成項目 |",
            "|---|---|---|",
            "| Input tensors | `src/Laya.Core/Inference/LayaInputTensorOwner.cs` | 依 raw lifecycle runs 核對建立失敗、重複 Dispose 與 worker ownership。 |",
            "| Run options／outputs | `src/Laya.Core/Inference/LayaInferenceRunner.cs` | 核對每 request／worker 的 deterministic dispose，並附上失敗 run IDs。 |",
            "| Session | `src/Laya.Core/Inference/LayaOnnxSession.cs` | 依 session lifecycle runs 檢閱 open／recreate／dispose 路徑。 |",
            "| Engine／tokenizer | `src/Laya.Core/Inference/LayaDecisionEngine.cs` | 檢閱長生命週期 owner 與 shared-engine dispose 路徑。 |",
            string.Empty,
            "Source code reference 不等於已完成 leak audit；Gate 前須記錄實際 findings 與 raw evidence。"
        };
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    /// <summary>要求 lifecycle audit、reproducibility 與 Docker 的 machine-readable disposition。</summary>
    private static void AddRequiredSupportingEvidenceBlockers(
        string evidenceRoot,
        string campaignId,
        ICollection<string> blockers)
    {
        foreach (var name in new[]
        {
            "phase3a-lifecycle-audit.json",
            "phase3a-reproducibility.json"
        })
        {
            if (!File.Exists(Path.Combine(evidenceRoot, name)))
            {
                blockers.Add($"Required machine-readable supporting evidence '{name}' is missing.");
            }
        }

        if (!File.Exists(Path.Combine(evidenceRoot, "docker", campaignId, "phase3a-docker-memory.json")))
        {
            blockers.Add("Required machine-readable Docker evidence is missing for this campaign.");
        }
    }

    /// <summary>輸出 Docker limit runs 或缺少 external container evidence 的 status report。</summary>
    private static void WriteDockerReport(string path, string evidenceRoot, string campaignId, ICollection<string> blockers)
    {
        var dockerEvidence = Path.Combine(evidenceRoot, "docker", campaignId, "phase3a-docker-memory.md");
        if (File.Exists(dockerEvidence))
        {
            if (!string.Equals(Path.GetFullPath(dockerEvidence), Path.GetFullPath(path), StringComparison.Ordinal))
            {
                File.Copy(dockerEvidence, path, overwrite: true);
            }

            return;
        }

        blockers.Add("Docker memory-limit matrix evidence is missing.");
        File.WriteAllLines(path, new[]
        {
            "# Phase 3A Docker 記憶體限制矩陣",
            string.Empty,
            "狀態：**BLOCKED — 缺少外部 Docker／cgroup 證據**",
            string.Empty,
            "必要限制（十進位 bytes）：2,000,000,000、2,500,000,000、3,000,000,000、4,000,000,000。",
            "報告須保存 inspect、OOMKilled、exit code、cgroup usage、process RSS 與 run manifest hashes。",
            "不以 native process 量測推測 Docker 結果。"
        }, new UTF8Encoding(false));
    }

    /// <summary>輸出 Gate report，明列 evidence status、classification、decision 與 blockers。</summary>
    private static void WriteGateReport(string path, IReadOnlyDictionary<string, object?> gate)
    {
        var blockers = (IEnumerable<string>)gate["blockers"]!;
        var lines = new List<string>
        {
            "# Phase 3A Memory Gate 報告",
            string.Empty,
            $"Evidence 狀態：**{gate["evidenceStatus"]}**",
            $"Memory Gate：**{gate["status"] ?? "BLOCKED"}**",
            $"Classification：**{gate["classification"] ?? "未分類"}**",
            $"Phase 3B 記憶體前置條件建議：**{gate["recommendation"]}**",
            string.Empty,
            "## 阻擋原因",
            string.Empty
        };
        lines.AddRange(blockers.Select(blocker => $"- {blocker}"));
        if (!blockers.Any())
        {
            lines.Add("- Validator 未記錄其他阻擋原因。 ");
        }

        lines.Add(string.Empty);
        lines.Add("本 Gate 不改變 Phase 2 品質／AUTO 限制，也不授權整合 BankReportImporter。 ");
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    /// <summary>輸出 aggregation index，列出 campaign、run manifests 與 aggregate reports。</summary>
    private static void WriteAggregateIndex(string outputRoot, string campaignId)
    {
        var reportFiles = Directory.GetFiles(outputRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path) != "phase3a-artifact-index.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = Path.GetFileName(path),
                ["sha256"] = HashFile(path),
                ["sizeBytes"] = new FileInfo(path).Length
            })
            .ToArray();
        var index = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["campaignId"] = campaignId,
            ["generatedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["reports"] = reportFiles
        };
        File.WriteAllText(
            Path.Combine(outputRoot, "phase3a-artifact-index.json"),
            JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    /// <summary>從 sample CSV header 找到欄位位置；缺欄位時回傳 -1。</summary>
    private static int FindHeaderIndex(IReadOnlyList<string> header, string name)
    {
        for (var index = 0; index < header.Count; index++)
        {
            if (string.Equals(header[index], name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>以一筆 CSV row 建立 name-value lookup。</summary>
    private static IEnumerable<Dictionary<string, string>> ReadSampleRows(string path)
    {
        using var reader = File.OpenText(path);
        using var records = CsvRecordReader.ReadRecords(reader).GetEnumerator();
        if (!records.MoveNext())
        {
            yield break;
        }

        var header = records.Current;
        while (records.MoveNext())
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < header.Count && index < records.Current.Count; index++)
            {
                row[header[index]] = records.Current[index];
            }

            yield return row;
        }
    }

    /// <summary>從 CSV row 讀取 invariant nullable integer。</summary>
    private static int? ParseInt(IReadOnlyDictionary<string, string> row, string name)
    {
        return row.TryGetValue(name, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>從 CSV row 讀取 invariant nullable Int64。</summary>
    private static long? ParseNullableLong(IReadOnlyDictionary<string, string> row, string name)
    {
        return row.TryGetValue(name, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>建立 missing slopes 的 blocked markdown cells。</summary>
    private static string Format(double? value)
    {
        return value?.ToString("R", CultureInfo.InvariantCulture) ?? "BLOCKED";
    }

    /// <summary>轉換內部 enum 為 Gate 契約定義的五種 exact classification names。</summary>
    private static string? FormatClassification(MemoryDiagnosticClassification? classification)
    {
        return classification switch
        {
            MemoryDiagnosticClassification.NoLeakEvidence => "No Leak Evidence",
            MemoryDiagnosticClassification.AllocatorPlateau => "Allocator Plateau",
            MemoryDiagnosticClassification.NativeRetentionSuspected => "Native Retention Suspected",
            MemoryDiagnosticClassification.LeakSuspected => "Leak Suspected",
            MemoryDiagnosticClassification.LeakConfirmed => "Leak Confirmed",
            _ => null
        };
    }

    /// <summary>讀取必要 non-empty JSON string property。</summary>
    private static string ReadRequiredString(JsonElement element, string name)
    {
        return element.GetProperty(name).GetString()
            ?? throw new InvalidDataException($"Evidence property '{name}' is null.");
    }

    /// <summary>讀取 optional JSON string property。</summary>
    private static string? ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.GetString();
    }

    /// <summary>計算 evidence artifact SHA-256。</summary>
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>建立 CSV-escaped row。</summary>
    private static void WriteCsvRow(TextWriter writer, IEnumerable<string?> values)
    {
        writer.WriteLine(string.Join(',', values.Select(value =>
        {
            if (value is null || (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r')))
            {
                return value ?? string.Empty;
            }

            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        })));
    }

    /// <summary>保存跨 stage 比對所需的固定實驗身分。</summary>
    private sealed record CampaignStageIdentity(
        string CampaignId,
        string Mode,
        string SourceCommit,
        bool SourceDirty,
        string? SourceDirtyIdentity,
        string? PolicySha256,
        string WorkloadSha256,
        string ModelRevision,
        string BundleManifestSha256,
        string TokenizerSha256,
        string DotnetRuntime,
        string OnnxRuntime);

    /// <summary>保存一份 campaign stage manifest 與其 required run ID set。</summary>
    private sealed class CampaignStageEvidence
    {
        /// <summary>建立 campaign stage evidence descriptor。</summary>
        public CampaignStageEvidence(string stage, string status, IReadOnlyList<string> runIds, string manifestPath)
        {
            Stage = stage;
            Status = status;
            RunIds = runIds;
            ManifestPath = manifestPath;
        }

        /// <summary>取得 stage 名稱。</summary>
        public string Stage { get; }

        /// <summary>取得 stage status。</summary>
        public string Status { get; }

        /// <summary>取得此 stage 要求的 run IDs。</summary>
        public IReadOnlyList<string> RunIds { get; }

        /// <summary>取得 campaign manifest path。</summary>
        public string ManifestPath { get; }
    }

    /// <summary>保存一份 raw per-run manifest 與 samples/latency locations。</summary>
    private sealed class RunEvidence
    {
        /// <summary>建立 child run evidence descriptor。</summary>
        public RunEvidence(
            string runId,
            string stage,
            string scenario,
            JsonElement manifest,
            string runDirectory,
            string? samplesPath,
            string? latencyPath)
        {
            RunId = runId;
            Stage = stage;
            Scenario = scenario;
            Manifest = manifest;
            RunDirectory = runDirectory;
            SamplesPath = samplesPath;
            LatencyPath = latencyPath;
        }

        /// <summary>取得 unique run ID。</summary>
        public string RunId { get; }

        /// <summary>取得 source campaign stage。</summary>
        public string Stage { get; }

        /// <summary>取得 executed scenario。</summary>
        public string Scenario { get; }

        /// <summary>取得 cloned raw run manifest JSON element。</summary>
        public JsonElement Manifest { get; }

        /// <summary>取得 run directory。</summary>
        public string RunDirectory { get; }

        /// <summary>取得 raw memory samples path。</summary>
        public string? SamplesPath { get; }

        /// <summary>取得 raw latency path。</summary>
        public string? LatencyPath { get; }
    }
}
