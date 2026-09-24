using System.Security.Cryptography;
using System.Text.Json;
using Laya.Core.Evaluation;
using Laya.Core.Exceptions;
using Laya.Shared.Configuration;

namespace Laya.ConsoleApp.Evaluation;

/// <summary>保存 frozen candidate 所引用的 development run artifact。</summary>
public sealed record LayaFrozenDevelopmentRun(
    string RunId,
    string ManifestPath,
    string ManifestSha256,
    string ResultsPath,
    string ResultsSha256);

/// <summary>保存已驗證、只能套用於 held-out 的 Phase 2 policy candidate。</summary>
public sealed record LayaFrozenCandidate(
    DateTimeOffset FrozenAtUtc,
    LayaFrozenDevelopmentRun DevelopmentRun,
    string Profile,
    string CheckpointRevision,
    string BundleManifestSha256,
    string ReferenceManifestPath,
    string ReferenceManifestSha256,
    string DatasetManifestSha256,
    string DatasetSha256,
    string GuidelineSha256,
    string DevelopmentSelectedIdsHash,
    string HeldOutSelectedIdsHash,
    LayaPromptVariant PromptVariant,
    string PromptHash,
    string OptionOrderHash,
    string SerializationVersion,
    string SerializationHash,
    LayaPhase2Policy Policy,
    string ManifestSha256);

/// <summary>在 engine 初始化前驗證 frozen candidate 的 artifact 與輸入身分。</summary>
public sealed class Phase2FrozenCandidateValidator
{
    /// <summary>讀取並完整驗證 candidate，成功時回傳帶有自身 hash 的不可變 contract。</summary>
    public LayaFrozenCandidate Validate(
        string candidatePath,
        string repositoryRoot,
        LayaProfileResolution profile,
        Phase2DatasetSelection development,
        Phase2DatasetSelection heldOut,
        string referenceManifestPath,
        LayaPromptVariant promptVariant,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(development);
        ArgumentNullException.ThrowIfNull(heldOut);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceManifestPath);

        var root = Path.GetFullPath(repositoryRoot);
        var absoluteCandidatePath = Path.IsPathRooted(candidatePath)
            ? Path.GetFullPath(candidatePath)
            : ResolvePath(root, candidatePath, "candidate");
        EnsureWithinRoot(root, absoluteCandidatePath, "candidate");
        using var document = ParseDocument(absoluteCandidatePath);
        ValidateUniqueProperties(document.RootElement, "candidate");
        var rootElement = document.RootElement;
        RequireInt(rootElement, "schemaVersion", 1);
        RequireString(rootElement, "kind", "laya.phase2.frozen-candidate");

        var frozenAtUtc = RequireDateTime(rootElement, "frozenAtUtc");
        var currentTime = now ?? DateTimeOffset.UtcNow;
        if (frozenAtUtc > currentTime)
        {
            throw Invalid("frozenAtUtc cannot be in the future.");
        }

        var developmentElement = RequireObject(rootElement, "developmentRun");
        var developmentRun = new LayaFrozenDevelopmentRun(
            RequireString(developmentElement, "runId"),
            RequireString(developmentElement, "manifestPath"),
            RequireSha256(developmentElement, "manifestSha256"),
            RequireString(developmentElement, "resultsPath"),
            RequireSha256(developmentElement, "resultsSha256"));
        ValidateDevelopmentRun(root, developmentRun, development, profile, promptVariant, frozenAtUtc);

        var model = RequireObject(rootElement, "model");
        var candidateProfile = RequireString(model, "profile");
        var checkpointRevision = RequireString(model, "checkpointRevision");
        var bundleManifestSha256 = RequireSha256(model, "bundleManifestSha256");
        if (!string.Equals(candidateProfile, profile.Name, StringComparison.Ordinal) ||
            !string.Equals(checkpointRevision, profile.CheckpointRevision, StringComparison.Ordinal))
        {
            throw Invalid("candidate model identity does not match the selected profile.");
        }

        var bundleManifestPath = Path.Combine(profile.ModelRoot, "laya-bundle-manifest.json");
        var actualBundleManifestSha256 = HashFile(bundleManifestPath, "bundle manifest");
        if (!string.Equals(bundleManifestSha256, actualBundleManifestSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("candidate bundle manifest hash does not match the selected model root.");
        }

        var reference = RequireObject(rootElement, "reference");
        var candidateReferencePath = RequireString(reference, "manifestPath");
        var candidateReferenceSha256 = RequireSha256(reference, "manifestSha256");
        var resolvedReferencePath = ResolvePath(root, candidateReferencePath, "reference manifest");
        var actualReferenceSha256 = HashFile(resolvedReferencePath, "reference manifest");
        if (!string.Equals(candidateReferenceSha256, actualReferenceSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(referenceManifestPath, root), resolvedReferencePath, StringComparison.Ordinal))
        {
            throw Invalid("candidate reference manifest identity does not match the readiness reference.");
        }

        var dataset = RequireObject(rootElement, "dataset");
        var datasetManifestSha256 = RequireSha256(dataset, "manifestSha256");
        var datasetSha256 = RequireSha256(dataset, "datasetSha256");
        var guidelineSha256 = RequireSha256(dataset, "guidelineSha256");
        var sourceSplit = RequireString(dataset, "sourceSplit");
        var targetSplit = RequireString(dataset, "targetSplit");
        var developmentSelectedIdsHash = RequireSha256(dataset, "developmentSelectedIdsHash");
        var heldOutSelectedIdsHash = RequireSha256(dataset, "heldOutSelectedIdsHash");
        if (sourceSplit != "development" || targetSplit != "held-out" ||
            !string.Equals(datasetManifestSha256, development.ManifestHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(datasetSha256, development.DatasetHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(guidelineSha256, development.GuidelineHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(developmentSelectedIdsHash, development.SelectedIdsHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(heldOutSelectedIdsHash, heldOut.SelectedIdsHash, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("candidate dataset or split identity does not match the current selection.");
        }

        var request = RequireObject(rootElement, "request");
        var candidatePrompt = ParsePromptVariant(RequireString(request, "promptVariant"));
        var promptHash = RequireSha256(request, "promptHash");
        var optionOrderHash = RequireSha256(request, "optionOrderHash");
        var serializationVersion = RequireString(request, "serializationVersion");
        var serializationHash = RequireSha256(request, "serializationHash");
        if (candidatePrompt != promptVariant ||
            !string.Equals(promptHash, LayaPhase2Contracts.PromptHash(promptVariant), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(optionOrderHash, LayaPhase2Contracts.OptionOrderHash(), StringComparison.OrdinalIgnoreCase) ||
            serializationVersion != LayaPhase2Contracts.SerializationVersion ||
            !string.Equals(serializationHash, LayaPhase2Contracts.SerializationHash(), StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("candidate request contract does not match the current Phase 2 request contract.");
        }

        var policy = ParsePolicy(RequireObject(rootElement, "policy"));
        var policyHash = RequireSha256(rootElement, "policyHash");
        if (!string.Equals(policyHash, LayaPhase2Contracts.PolicyHash(policy), StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("candidate policy hash does not match its thresholds.");
        }
        ValidateDevelopmentPolicy(root, developmentRun, policyHash);

        var candidateHash = HashFile(absoluteCandidatePath, "frozen candidate");
        return new LayaFrozenCandidate(
            frozenAtUtc,
            developmentRun,
            candidateProfile,
            checkpointRevision,
            bundleManifestSha256,
            candidateReferencePath,
            candidateReferenceSha256,
            datasetManifestSha256,
            datasetSha256,
            guidelineSha256,
            developmentSelectedIdsHash,
            heldOutSelectedIdsHash,
            candidatePrompt,
            promptHash,
            optionOrderHash,
            serializationVersion,
            serializationHash,
            policy,
            candidateHash);
    }

    /// <summary>驗證 development run artifact 的 hash、完整狀態與固定 request identity。</summary>
    private static void ValidateDevelopmentRun(
        string repositoryRoot,
        LayaFrozenDevelopmentRun candidate,
        Phase2DatasetSelection development,
        LayaProfileResolution profile,
        LayaPromptVariant promptVariant,
        DateTimeOffset frozenAtUtc)
    {
        var manifestPath = ResolvePath(repositoryRoot, candidate.ManifestPath, "development manifest");
        var resultsPath = ResolvePath(repositoryRoot, candidate.ResultsPath, "development results");
        if (!string.Equals(HashFile(manifestPath, "development manifest"), candidate.ManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(HashFile(resultsPath, "development results"), candidate.ResultsSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("development run artifact hash mismatch.");
        }

        using var manifest = ParseDocument(manifestPath);
        using var results = ParseDocument(resultsPath);
        var manifestRoot = manifest.RootElement;
        var runId = RequireStringAny(manifestRoot, "runId", "RunId");
        if (runId != candidate.RunId ||
            RequireStringAny(manifestRoot, "profile", "Profile") != profile.Name ||
            RequireStringAny(manifestRoot, "modelRevision", "ModelRevision") != profile.CheckpointRevision ||
            RequireStringAny(manifestRoot, "datasetHash", "DatasetHash") != development.DatasetHash ||
            RequireStringAny(manifestRoot, "guidelineHash", "GuidelineHash") != development.GuidelineHash ||
            RequireStringAny(manifestRoot, "datasetManifestHash", "DatasetManifestHash") != development.ManifestHash ||
            RequireStringAny(manifestRoot, "split", "Split") != "development" ||
            RequireStringAny(manifestRoot, "promptVariant", "PromptVariant") != promptVariant.ToString() ||
            RequireStringAny(manifestRoot, "promptHash", "PromptHash") != LayaPhase2Contracts.PromptHash(promptVariant) ||
            RequireStringAny(manifestRoot, "selectedIdsHash", "SelectedIdsHash") != development.SelectedIdsHash)
        {
            throw Invalid("development run manifest identity does not match the frozen candidate.");
        }

        var selectedIds = RequireStringArrayAny(manifestRoot, "selectedIds", "SelectedIds");
        if (!selectedIds.SequenceEqual(development.SelectedIds, StringComparer.Ordinal) ||
            !string.Equals(
                LayaPhase2Contracts.Hash(JsonSerializer.SerializeToUtf8Bytes(selectedIds)),
                development.SelectedIdsHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("development selected IDs do not match the frozen candidate dataset.");
        }

        var createdUtc = RequireDateTimeAny(manifestRoot, "createdUtc", "CreatedUtc");
        if (createdUtc > frozenAtUtc)
        {
            throw Invalid("frozenAtUtc precedes the referenced development run.");
        }

        var resultsManifest = RequireObjectAny(results.RootElement, "manifest", "Manifest");
        if (RequireStringAny(resultsManifest, "runId", "RunId") != candidate.RunId ||
            !RequireBooleanAny(results.RootElement, "complete", "Complete"))
        {
            throw Invalid("development results are incomplete or reference a different run.");
        }
    }

    /// <summary>確認 development run 已保存與 candidate 相同的 policy hash。</summary>
    private static void ValidateDevelopmentPolicy(
        string repositoryRoot,
        LayaFrozenDevelopmentRun candidate,
        string policyHash)
    {
        var manifestPath = ResolvePath(repositoryRoot, candidate.ManifestPath, "development manifest");
        using var manifest = ParseDocument(manifestPath);
        if (!string.Equals(
                RequireSha256Any(manifest.RootElement, "policyHash", "PolicyHash"),
                policyHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("development run policy hash differs from frozen candidate.");
        }
    }

    /// <summary>解析候選 policy 並拒絕非有限或超出範圍的 threshold。</summary>
    private static LayaPhase2Policy ParsePolicy(JsonElement element)
    {
        var policy = new LayaPhase2Policy(
            RequireString(element, "name"),
            RequireBoolean(element, "autoEnabled"),
            RequireDouble(element, "autoProbabilityThreshold"),
            RequireDouble(element, "autoMarginThreshold"),
            RequireDouble(element, "suggestProbabilityThreshold"),
            RequireDouble(element, "suggestMarginThreshold"));
        _ = LayaPolicyEvaluator.ClassifyFailure(policy);
        return policy;
    }

    /// <summary>解析 prompt variant 的固定名稱。</summary>
    private static LayaPromptVariant ParsePromptVariant(string value)
    {
        return Enum.TryParse<LayaPromptVariant>(value, ignoreCase: false, out var result)
            ? result
            : throw Invalid($"Unknown candidate prompt variant '{value}'.");
    }

    /// <summary>讀取 JSON 並將格式錯誤轉成 configuration blocker。</summary>
    private static JsonDocument ParseDocument(string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw Invalid($"Could not read JSON artifact '{path}': {exception.Message}");
        }
    }

    /// <summary>拒絕重複 JSON property，避免 candidate identity 被最後一值覆蓋。</summary>
    private static void ValidateUniqueProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw Invalid($"Duplicate candidate property '{path}.{property.Name}'.");
                }

                ValidateUniqueProperties(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateUniqueProperties(item, path);
            }
        }
    }

    /// <summary>解析 repository 內的相對 artifact path，阻擋 path traversal。</summary>
    private static string ResolvePath(string root, string path, string kind)
    {
        if (Path.IsPathRooted(path))
        {
            throw Invalid($"{kind} path must be repository-relative.");
        }

        var absolutePath = Path.GetFullPath(path, root);
        EnsureWithinRoot(root, absolutePath, kind);
        return absolutePath;
    }

    /// <summary>確認 artifact 位於 repository root 內，避免絕對路徑繞過邊界。</summary>
    private static void EnsureWithinRoot(string root, string path, string kind)
    {
        var absoluteRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var absolutePath = Path.GetFullPath(path);
        if (!absolutePath.StartsWith(absoluteRoot, StringComparison.Ordinal))
        {
            throw Invalid($"{kind} path escapes repository root.");
        }
    }

    /// <summary>以串流 hash 驗證 artifact 存在並回傳 SHA-256。</summary>
    private static string HashFile(string path, string kind)
    {
        if (!File.Exists(path))
        {
            throw Invalid($"Missing {kind}: {path}");
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>讀取指定 object property。</summary>
    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"Candidate property '{name}' must be an object.");
        }

        return value;
    }

    /// <summary>讀取可接受 camelCase 或 PascalCase 的 object property。</summary>
    private static JsonElement RequireObjectAny(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                return value;
            }
        }

        throw Invalid($"JSON object is missing required property '{names[0]}'.");
    }

    /// <summary>讀取非空字串 property。</summary>
    private static string RequireString(JsonElement parent, string name, string? expected = null)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid($"Candidate property '{name}' must be a non-empty string.");
        }

        var result = value.GetString()!;
        if (expected is not null && !string.Equals(result, expected, StringComparison.Ordinal))
        {
            throw Invalid($"Candidate property '{name}' must equal '{expected}'.");
        }

        return result;
    }

    /// <summary>讀取 camelCase 或 PascalCase 的非空字串 property。</summary>
    private static string RequireStringAny(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }
        }

        throw Invalid($"JSON object is missing required string property '{names[0]}'.");
    }

    /// <summary>讀取 camelCase 或 PascalCase 的非空字串陣列。</summary>
    private static string[] RequireStringArrayAny(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                var values = value.EnumerateArray().Select(item =>
                {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        throw Invalid($"JSON array property '{name}' contains an invalid ID.");
                    }

                    return item.GetString()!;
                }).ToArray();
                return values;
            }
        }

        throw Invalid($"JSON object is missing required string array property '{names[0]}'.");
    }

    /// <summary>讀取固定整數 property。</summary>
    private static void RequireInt(JsonElement parent, string name, int expected)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var actual) || actual != expected)
        {
            throw Invalid($"Candidate property '{name}' must equal {expected}.");
        }
    }

    /// <summary>讀取 bool property。</summary>
    private static bool RequireBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            throw Invalid($"Candidate property '{name}' must be boolean.");
        }

        return value.GetBoolean();
    }

    /// <summary>讀取 camelCase 或 PascalCase 的 bool property。</summary>
    private static bool RequireBooleanAny(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }
        }

        throw Invalid($"JSON object is missing required boolean property '{names[0]}'.");
    }

    /// <summary>讀取有限且位於 [0,1] 的 double threshold。</summary>
    private static double RequireDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) ||
            !double.IsFinite(result) || result < 0 || result > 1)
        {
            throw Invalid($"Candidate property '{name}' must be a finite number in [0,1].");
        }

        return result;
    }

    /// <summary>讀取合法 UTC timestamp。</summary>
    private static DateTimeOffset RequireDateTime(JsonElement parent, string name)
    {
        var value = RequireString(parent, name);
        if (!DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var result))
        {
            throw Invalid($"Candidate property '{name}' must be a valid timestamp.");
        }

        return result.ToUniversalTime();
    }

    /// <summary>讀取 camelCase 或 PascalCase timestamp。</summary>
    private static DateTimeOffset RequireDateTimeAny(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var result))
            {
                return result.ToUniversalTime();
            }
        }

        throw Invalid($"JSON object is missing required timestamp property '{names[0]}'.");
    }

    /// <summary>讀取 lowercase SHA-256 格式字串。</summary>
    private static string RequireSha256(JsonElement parent, string name)
    {
        var value = RequireString(parent, name);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw Invalid($"Candidate property '{name}' must be a SHA-256 hex string.");
        }

        return value.ToLowerInvariant();
    }

    /// <summary>讀取 camelCase 或 PascalCase 的 SHA-256 property。</summary>
    private static string RequireSha256Any(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var candidate = value.GetString();
                if (candidate?.Length == 64 && candidate.All(Uri.IsHexDigit))
                {
                    return candidate.ToLowerInvariant();
                }
            }
        }

        throw Invalid($"JSON object is missing required SHA-256 property '{names[0]}'.");
    }

    /// <summary>建立統一的 candidate configuration exception。</summary>
    private static LayaConfigurationException Invalid(string message)
    {
        return new LayaConfigurationException($"--frozen-candidate is blocked: invalid candidate: {message}");
    }
}
