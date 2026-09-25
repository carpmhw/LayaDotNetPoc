using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Laya.MemoryBenchmarks.Analysis;
using Laya.MemoryBenchmarks.Reporting;
using Laya.MemoryProbe.Reporting;
using Laya.Shared.Configuration;

namespace Laya.MemoryBenchmarks.Orchestration;

/// <summary>執行分 stage campaign，驗證 resume identities 並保存 child process evidence。</summary>
internal static class MemoryCampaignRunner
{
    /// <summary>建立或 resume 一個 stage campaign，逐一啟動新 Probe process。</summary>
    public static async Task<int> ExecuteAsync(
        MemoryCampaignOptions options,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        if (options.Stage == "aggregate")
        {
            var report = MemoryCampaignReportAggregator.Aggregate(
                options.OutputRoot,
                options.OutputRoot,
                options.CampaignId);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                campaignId = options.CampaignId,
                evidenceStatus = report.EvidenceStatus,
                status = report.GateStatus,
                classification = report.Classification?.ToString(),
                recommendation = report.Recommendation,
                blockers = report.Blockers
            }));
            return report.EvidenceStatus == "complete" ? 0 : 2;
        }

        var root = Path.GetFullPath(repositoryRoot);
        var profile = LayaProfileResolver.Resolve(
            options.ModelRoot,
            "multilingual",
            LayaProfileResolver.LoadEnvironment(),
            root);
        var environment = MemoryProbeHostMetadata.CaptureEnvironment();
        var sourceIdentity = MemoryProbeHostMetadata.CaptureSourceIdentity(root);
        var workloadManifestPath = Path.Combine(root, "test-data", "phase3a", "memory-workloads-manifest.json");
        using var workloadManifest = JsonDocument.Parse(File.ReadAllText(workloadManifestPath));
        var workloadHash = workloadManifest.RootElement.GetProperty("workload").GetProperty("sha256").GetString()
            ?? throw new InvalidDataException("Workload manifest has no SHA-256.");
        var bundleManifestPath = Path.Combine(profile.ModelRoot, "laya-bundle-manifest.json");
        using var bundleManifest = JsonDocument.Parse(File.ReadAllText(bundleManifestPath));
        var bundleManifestHash = HashFile(bundleManifestPath);
        var tokenizerHash = bundleManifest.RootElement.GetProperty("files")
            .GetProperty("tokenizer/tokenizer.json")
            .GetProperty("sha256")
            .GetString()!;
        var onnxRuntime = environment["onnxRuntime"]?.ToString() ?? "unknown";
        var dotNetRuntime = environment["dotnetRuntime"]?.ToString() ?? "unknown";
        var policyHash = options.Policy?.Sha256;
        var identity = new MemoryCampaignIdentity(
            sourceIdentity["commit"]!.ToString()!,
            (bool)sourceIdentity["dirty"]!,
            sourceIdentity["dirtyIdentity"] as string,
            policyHash,
            workloadHash,
            profile.CheckpointRevision!,
            bundleManifestHash,
            tokenizerHash,
            dotNetRuntime,
            onnxRuntime,
            options.Mode);
        var plan = MemoryCampaignPlanner.CreatePlan(options.Stage, options.CampaignId, options.Mode);
        var campaignDirectory = Path.Combine(options.OutputRoot, "campaigns", options.CampaignId, options.Stage);
        var campaignManifestPath = Path.Combine(campaignDirectory, "campaign.json");
        var campaignDirectoryExists = Directory.Exists(campaignDirectory);
        if (campaignDirectoryExists && !options.Resume)
        {
            throw new IOException($"Campaign stage already exists; use --resume with the same identity: {campaignDirectory}");
        }

        if (options.Resume && campaignDirectoryExists && !File.Exists(campaignManifestPath))
        {
            throw new InvalidDataException("Existing campaign stage has no manifest and cannot be resumed.");
        }

        Directory.CreateDirectory(campaignDirectory);
        using var campaignLock = new FileStream(
            Path.Combine(campaignDirectory, ".campaign-lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        var manifest = CreateCampaignManifest(options, plan, profile, identity, sourceIdentity, environment, workloadHash, bundleManifestHash, tokenizerHash);
        if (options.Resume && File.Exists(campaignManifestPath))
        {
            using var existing = JsonDocument.Parse(File.ReadAllText(campaignManifestPath));
            ValidateCampaignResume(existing.RootElement, options, plan, identity);
            CopyCompletedSteps(existing.RootElement, manifest);
        }

        var hostLogRoot = Path.Combine(campaignDirectory, "host-logs");
        Directory.CreateDirectory(hostLogRoot);
        WriteManifestAtomic(campaignManifestPath, manifest);

        foreach (var invocation in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exitCode = await ExecuteInvocationAsync(
                options,
                root,
                profile.ModelRoot,
                identity,
                invocation,
                campaignDirectory,
                hostLogRoot,
                manifest,
                cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        if (options.Mode == "formal" && options.Stage == "reproduction")
        {
            var measurements = plan
                .Where(invocation => invocation.ReplicationGroup is not null)
                .ToDictionary(
                    invocation => invocation.RunId,
                    invocation => ReadReplicateMetrics(options.OutputRoot, invocation),
                    StringComparer.Ordinal);
            var thirdRuns = MemoryCampaignReproductionPlanner.PlanThirdReplicates(
                plan,
                measurements,
                options.Policy!.ReplicateTolerance);
            foreach (var thirdReplicate in thirdRuns)
            {
                plan = plan.Append(thirdReplicate).ToArray();
                var requiredRunIds = ((IEnumerable<string>)manifest["requiredRunIds"]!)
                    .Append(thirdReplicate.RunId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                manifest["requiredRunIds"] = requiredRunIds;
                GetStageStatus(manifest).TryAdd(thirdReplicate.StepId, "pending");
                WriteManifestAtomic(campaignManifestPath, manifest);

                var thirdExitCode = await ExecuteInvocationAsync(
                    options,
                    root,
                    profile.ModelRoot,
                    identity,
                    thirdReplicate,
                    campaignDirectory,
                    hostLogRoot,
                    manifest,
                    cancellationToken).ConfigureAwait(false);
                if (thirdExitCode != 0)
                {
                    return thirdExitCode;
                }

                measurements[thirdReplicate.RunId] = ReadReplicateMetrics(options.OutputRoot, thirdReplicate);
            }

            foreach (var group in plan.Where(invocation => invocation.ReplicationGroup is not null)
                         .GroupBy(invocation => invocation.ReplicationGroup!, StringComparer.Ordinal))
            {
                var assessment = MemoryReplicateAnalyzer.Assess(
                    group.Select(invocation => measurements[invocation.RunId]).ToArray(),
                    options.Policy.ReplicateTolerance);
                if (!assessment.IsConsistent)
                {
                    manifest["evidenceStatus"] = "blocked";
                    AddBlocker(manifest, $"Reproduction group '{group.Key}' is inconsistent: {assessment.Reason}");
                    WriteManifestAtomic(campaignManifestPath, manifest);
                }
            }
        }

        var campaignEvidenceStatus = manifest["evidenceStatus"]?.ToString();
        if (campaignEvidenceStatus != "blocked")
        {
            campaignEvidenceStatus = "complete";
            manifest["evidenceStatus"] = campaignEvidenceStatus;
        }

        WriteManifestAtomic(campaignManifestPath, manifest);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            campaignId = options.CampaignId,
            stage = options.Stage,
            status = campaignEvidenceStatus,
            runCount = plan.Count,
            campaignManifestPath
        }));
        return campaignEvidenceStatus == "complete" ? 0 : 2;
    }

    /// <summary>執行或驗證一個 invocation，並只在 complete raw evidence 後標記 step complete。</summary>
    private static async Task<int> ExecuteInvocationAsync(
        MemoryCampaignOptions options,
        string repositoryRoot,
        string modelRoot,
        MemoryCampaignIdentity identity,
        MemoryProbeInvocation invocation,
        string campaignDirectory,
        string hostLogRoot,
        Dictionary<string, object?> manifest,
        CancellationToken cancellationToken)
    {
        var runDirectory = Path.Combine(options.OutputRoot, "runs", invocation.RunId);
        var stepStatus = GetStageStatus(manifest);
        if (Directory.Exists(runDirectory))
        {
            if (!options.Resume)
            {
                throw new IOException($"Probe run directory already exists and cannot be overwritten: {runDirectory}");
            }

            var validation = MemoryCampaignResumeValidator.ValidateRunDirectory(runDirectory, identity);
            if (!validation.IsReusable)
            {
                throw new InvalidDataException($"Run '{invocation.RunId}' cannot be resumed: {validation.Reason}");
            }

            stepStatus[invocation.StepId] = "complete";
            WriteManifestAtomic(Path.Combine(campaignDirectory, "campaign.json"), manifest);
            return 0;
        }

        if (options.Resume && stepStatus.GetValueOrDefault(invocation.StepId) == "complete")
        {
            throw new InvalidDataException($"Campaign step '{invocation.StepId}' is marked complete but its run directory is missing.");
        }

        stepStatus[invocation.StepId] = "running";
        WriteManifestAtomic(Path.Combine(campaignDirectory, "campaign.json"), manifest);
        var childResult = await MemoryCampaignProcessRunner.RunAsync(
            repositoryRoot,
            invocation,
            options.OutputRoot,
            hostLogRoot,
            modelRoot,
            options.PolicyPath,
            cancellationToken,
            options.CampaignId,
            identity).ConfigureAwait(false);
        AddHostLogArtifacts(manifest, campaignDirectory, childResult);
        if (childResult.ExitCode != 0)
        {
            stepStatus[invocation.StepId] = "failed";
            manifest["evidenceStatus"] = "blocked";
            AddBlocker(manifest, $"Probe run '{invocation.RunId}' exited with code {childResult.ExitCode}.");
            WriteManifestAtomic(Path.Combine(campaignDirectory, "campaign.json"), manifest);
            return childResult.ExitCode;
        }

        var runValidation = MemoryCampaignResumeValidator.ValidateRunDirectory(runDirectory, identity);
        if (!runValidation.IsReusable)
        {
            stepStatus[invocation.StepId] = "failed";
            manifest["evidenceStatus"] = "blocked";
            AddBlocker(manifest, $"Probe run '{invocation.RunId}' failed evidence validation: {runValidation.Reason}");
            WriteManifestAtomic(Path.Combine(campaignDirectory, "campaign.json"), manifest);
            return 2;
        }

        stepStatus[invocation.StepId] = "complete";
        WriteManifestAtomic(Path.Combine(campaignDirectory, "campaign.json"), manifest);
        return 0;
    }

    /// <summary>由 raw sample CSV 重算一份重現性 comparison metrics。</summary>
    private static MemoryReplicateMeasurement ReadReplicateMetrics(
        string outputRoot,
        MemoryProbeInvocation invocation)
    {
        var samplesPath = Path.Combine(outputRoot, "runs", invocation.RunId, "phase3a-memory-samples.csv");
        return MemoryCampaignMeasurementReader.ReadRunMetrics(invocation.RunId, samplesPath);
    }

    /// <summary>建立 schema 相容 campaign manifest 與所有 planned run IDs。</summary>
    private static Dictionary<string, object?> CreateCampaignManifest(
        MemoryCampaignOptions options,
        IReadOnlyList<MemoryProbeInvocation> plan,
        LayaProfileResolution profile,
        MemoryCampaignIdentity identity,
        IReadOnlyDictionary<string, object?> sourceIdentity,
        IReadOnlyDictionary<string, object?> environment,
        string workloadHash,
        string bundleManifestHash,
        string tokenizerHash)
    {
        var policy = options.Policy is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = options.PolicyPath,
                ["sha256"] = options.Policy.Sha256,
                ["targetMemoryLimitBytes"] = options.Policy.TargetMemoryLimitBytes
            };
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["campaignId"] = options.CampaignId,
            ["stage"] = options.Stage,
            ["mode"] = options.Mode,
            ["evidenceStatus"] = "in-progress",
            ["createdUtc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["sourceIdentity"] = sourceIdentity,
            ["policy"] = policy,
            ["workload"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = "test-data/phase3a/memory-workloads.json",
                ["sha256"] = workloadHash,
                ["profile"] = profile.Name,
                ["questionCount"] = 1,
                ["questionCounts"] = new[] { 1, 2, 5 },
                ["serializationVersion"] = "laya-state-v1"
            },
            ["model"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["profile"] = profile.Name,
                ["revision"] = profile.CheckpointRevision,
                ["bundleRoot"] = profile.ModelRoot,
                ["bundleManifestSha256"] = bundleManifestHash,
                ["tokenizerSha256"] = tokenizerHash
            },
            ["environment"] = environment,
            ["requiredRunIds"] = plan.Select(invocation => invocation.RunId).ToArray(),
            ["stageStatus"] = plan.ToDictionary(invocation => invocation.StepId, _ => "pending", StringComparer.Ordinal),
            ["blockers"] = Array.Empty<string>(),
            ["artifacts"] = new List<object>()
        };
    }

    /// <summary>比較 existing campaign manifest 與目前 campaign immutable identities 及 run set。</summary>
    private static void ValidateCampaignResume(
        JsonElement existing,
        MemoryCampaignOptions options,
        IReadOnlyList<MemoryProbeInvocation> plan,
        MemoryCampaignIdentity expected)
    {
        if (!string.Equals(existing.GetProperty("campaignId").GetString(), options.CampaignId, StringComparison.Ordinal) ||
            !string.Equals(existing.GetProperty("stage").GetString(), options.Stage, StringComparison.Ordinal) ||
            !string.Equals(existing.GetProperty("mode").GetString(), options.Mode, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Existing campaign ID, stage or mode does not match the requested resume.");
        }

        var source = existing.GetProperty("sourceIdentity");
        if (!string.Equals(source.GetProperty("commit").GetString(), expected.SourceCommit, StringComparison.Ordinal) ||
            source.GetProperty("dirty").GetBoolean() != expected.SourceDirty ||
            ReadOptionalString(source, "dirtyIdentity") != expected.DirtyIdentity)
        {
            throw new InvalidDataException("Existing campaign source identity differs from the current worktree.");
        }

        var existingPolicy = existing.GetProperty("policy");
        var existingPolicyHash = existingPolicy.ValueKind == JsonValueKind.Null
            ? null
            : existingPolicy.GetProperty("sha256").GetString();
        var workload = existing.GetProperty("workload");
        var model = existing.GetProperty("model");
        var environment = existing.GetProperty("environment");
        var runIds = existing.GetProperty("requiredRunIds").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        var expectedRunIds = plan.Select(invocation => invocation.RunId).ToArray();
        var missingRunIds = expectedRunIds.Except(runIds, StringComparer.Ordinal).ToArray();
        var additionalRunIds = runIds.Except(expectedRunIds, StringComparer.Ordinal).ToArray();
        var hasOnlyThirdReplicates = additionalRunIds.Length == 0 ||
            (options.Stage == "reproduction" &&
             additionalRunIds.All(runId => IsThirdReplicateRunId(runId, options.CampaignId)));
        if (existingPolicyHash != expected.PolicySha256)
        {
            throw new InvalidDataException("Existing campaign frozen policy hash differs.");
        }

        if (!string.Equals(workload.GetProperty("sha256").GetString(), expected.WorkloadSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Existing campaign workload fixture hash differs.");
        }

        if (!string.Equals(model.GetProperty("revision").GetString(), expected.ModelRevision, StringComparison.Ordinal) ||
            !string.Equals(model.GetProperty("bundleManifestSha256").GetString(), expected.BundleManifestSha256, StringComparison.Ordinal) ||
            !string.Equals(model.GetProperty("tokenizerSha256").GetString(), expected.TokenizerSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Existing campaign verified model identity differs.");
        }

        if (!string.Equals(environment.GetProperty("dotnetRuntime").GetString(), expected.DotNetRuntime, StringComparison.Ordinal) ||
            !string.Equals(environment.GetProperty("onnxRuntime").GetString(), expected.OnnxRuntime, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Existing campaign runtime identity differs.");
        }

        if (missingRunIds.Length != 0 || !hasOnlyThirdReplicates)
        {
            throw new InvalidDataException("Existing campaign run matrix differs from the requested stage plan.");
        }
    }

    /// <summary>保留 existing completed step states 與已保存 artifacts。</summary>
    private static void CopyCompletedSteps(JsonElement existing, IDictionary<string, object?> manifest)
    {
        var status = (Dictionary<string, string>)manifest["stageStatus"]!;
        var existingStatus = existing.GetProperty("stageStatus");
        foreach (var property in existingStatus.EnumerateObject())
        {
            if (property.Value.GetString() == "complete" &&
                (status.ContainsKey(property.Name) || property.Name.Contains("-r3", StringComparison.Ordinal)))
            {
                status[property.Name] = "complete";
            }
        }

        manifest["requiredRunIds"] = existing.GetProperty("requiredRunIds")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

        if (existing.TryGetProperty("artifacts", out var artifacts))
        {
            manifest["artifacts"] = artifacts.EnumerateArray().Select(item => (object)item.Clone()).ToList();
        }

        if (existing.TryGetProperty("createdUtc", out var createdUtc))
        {
            manifest["createdUtc"] = createdUtc.GetString();
        }
    }

    /// <summary>確認 campaign ID 下的 extra run ID 僅為合法第三次 reproduction replicate。</summary>
    private static bool IsThirdReplicateRunId(string runId, string campaignId)
    {
        return runId.StartsWith($"{campaignId}-repro-", StringComparison.Ordinal) &&
            runId.EndsWith("-r3", StringComparison.Ordinal);
    }

    /// <summary>取得 campaign stage status map。</summary>
    private static Dictionary<string, string> GetStageStatus(IDictionary<string, object?> manifest)
    {
        return (Dictionary<string, string>)manifest["stageStatus"]!;
    }

    /// <summary>將 blocker reason 加入 manifest 並保留先前 blocker notes。</summary>
    private static void AddBlocker(IDictionary<string, object?> manifest, string blocker)
    {
        var blockers = manifest["blockers"] as List<string> ?? new List<string>();
        blockers.Add(blocker);
        manifest["blockers"] = blockers;
    }

    /// <summary>加入 child stdout／stderr hash entries 至 campaign artifacts index。</summary>
    private static void AddHostLogArtifacts(
        IDictionary<string, object?> manifest,
        string campaignDirectory,
        MemoryCampaignChildResult childResult)
    {
        var artifacts = manifest["artifacts"] as List<object> ?? new List<object>();
        artifacts.Add(CreateArtifact(campaignDirectory, childResult.StandardOutputPath));
        artifacts.Add(CreateArtifact(campaignDirectory, childResult.StandardErrorPath));
        manifest["artifacts"] = artifacts;
    }

    /// <summary>建立一筆相對 campaign root 的 host log artifact index。</summary>
    private static Dictionary<string, object?> CreateArtifact(string root, string path)
    {
        using var stream = File.OpenRead(path);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = Path.GetRelativePath(root, path).Replace('\\', '/'),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            ["sizeBytes"] = new FileInfo(path).Length,
            ["status"] = "complete",
            ["unavailableReason"] = null
        };
    }

    /// <summary>讀取 object optional string property。</summary>
    private static string? ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.GetString();
    }

    /// <summary>原子寫入 campaign JSON，避免中斷留下半份 manifest。</summary>
    private static void WriteManifestAtomic(string path, object manifest)
    {
        var temporaryPath = path + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>計算 model／workload artifact file SHA-256。</summary>
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
