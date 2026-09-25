using System.Security.Cryptography;
using System.Text.Json;

namespace Laya.MemoryBenchmarks.Orchestration;

/// <summary>保存不可跨 campaign resume 的 source／policy／workload／model／runtime identity。</summary>
internal sealed class MemoryCampaignIdentity
{
    /// <summary>建立一組完整不可變的 campaign identity。</summary>
    public MemoryCampaignIdentity(
        string sourceCommit,
        bool sourceDirty,
        string? dirtyIdentity,
        string? policySha256,
        string workloadSha256,
        string modelRevision,
        string bundleManifestSha256,
        string tokenizerSha256,
        string dotNetRuntime,
        string onnxRuntime,
        string mode = "formal")
    {
        SourceCommit = sourceCommit;
        SourceDirty = sourceDirty;
        DirtyIdentity = dirtyIdentity;
        PolicySha256 = policySha256;
        WorkloadSha256 = workloadSha256;
        ModelRevision = modelRevision;
        BundleManifestSha256 = bundleManifestSha256;
        TokenizerSha256 = tokenizerSha256;
        DotNetRuntime = dotNetRuntime;
        OnnxRuntime = onnxRuntime;
        Mode = mode;
    }

    /// <summary>取得 source commit。</summary>
    public string SourceCommit { get; }

    /// <summary>取得 source tree dirty 狀態。</summary>
    public bool SourceDirty { get; }

    /// <summary>取得 dirty source content hash。</summary>
    public string? DirtyIdentity { get; }

    /// <summary>取得 frozen policy hash；pilot 無 policy 時為 null。</summary>
    public string? PolicySha256 { get; }

    /// <summary>取得 workload fixture hash。</summary>
    public string WorkloadSha256 { get; }

    /// <summary>取得 model checkpoint revision。</summary>
    public string ModelRevision { get; }

    /// <summary>取得 verified bundle manifest hash。</summary>
    public string BundleManifestSha256 { get; }

    /// <summary>取得 tokenizer hash。</summary>
    public string TokenizerSha256 { get; }

    /// <summary>取得 .NET runtime identity。</summary>
    public string DotNetRuntime { get; }

    /// <summary>取得 ONNX Runtime version。</summary>
    public string OnnxRuntime { get; }

    /// <summary>取得 pilot／formal run mode。</summary>
    public string Mode { get; }
}

/// <summary>保存是否可以 reuse 舊 run 與不可相容原因。</summary>
internal sealed class MemoryCampaignResumeValidation
{
    /// <summary>建立 run resume validation result。</summary>
    public MemoryCampaignResumeValidation(bool isReusable, string? reason)
    {
        IsReusable = isReusable;
        Reason = reason;
    }

    /// <summary>取得既有 evidence 是否可安全 reuse。</summary>
    public bool IsReusable { get; }

    /// <summary>取得不可 reuse 的第一個診斷原因。</summary>
    public string? Reason { get; }
}

/// <summary>拒絕不完整、stale、tampered 或 identity 不同的 resume runs。</summary>
internal static class MemoryCampaignResumeValidator
{
    /// <summary>驗證 in-memory manifest 的 source、policy、workload、bundle 與 runtime identity。</summary>
    public static MemoryCampaignResumeValidation Validate(
        JsonElement manifest,
        MemoryCampaignIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            if (manifest.GetProperty("schemaVersion").GetInt32() != 1)
            {
                return Reject("Run manifest schema version is unsupported.");
            }

            if (!string.Equals(ReadString(manifest, "status"), "complete", StringComparison.Ordinal))
            {
                return Reject("Only complete run manifests can be reused.");
            }

            if (!string.Equals(ReadString(manifest, "mode"), expected.Mode, StringComparison.Ordinal))
            {
                return Reject("Run mode differs from the requested campaign.");
            }

            var source = manifest.GetProperty("sourceIdentity");
            if (!string.Equals(ReadString(source, "commit"), expected.SourceCommit, StringComparison.Ordinal) ||
                source.GetProperty("dirty").GetBoolean() != expected.SourceDirty ||
                ReadOptionalString(source, "dirtyIdentity") != expected.DirtyIdentity)
            {
                return Reject("Source commit or dirty identity differs from the requested campaign.");
            }

            if (ReadOptionalString(manifest, "policySha256") != expected.PolicySha256)
            {
                return Reject("Frozen policy hash differs from the requested campaign.");
            }

            if (!string.Equals(ReadString(manifest, "workloadSha256"), expected.WorkloadSha256, StringComparison.Ordinal))
            {
                return Reject("Workload fixture hash differs from the requested campaign.");
            }

            var model = manifest.GetProperty("model");
            if (!string.Equals(ReadString(model, "profile"), "multilingual", StringComparison.Ordinal) ||
                !string.Equals(ReadString(model, "revision"), expected.ModelRevision, StringComparison.Ordinal) ||
                !string.Equals(ReadString(model, "bundleManifestSha256"), expected.BundleManifestSha256, StringComparison.Ordinal) ||
                !string.Equals(ReadString(model, "tokenizerSha256"), expected.TokenizerSha256, StringComparison.Ordinal))
            {
                return Reject("Model checkpoint, verified bundle or tokenizer identity differs from the requested campaign.");
            }

            var environment = manifest.GetProperty("environment");
            if (!string.Equals(ReadString(environment, "dotnetRuntime"), expected.DotNetRuntime, StringComparison.Ordinal) ||
                !string.Equals(ReadString(environment, "onnxRuntime"), expected.OnnxRuntime, StringComparison.Ordinal) ||
                !string.Equals(ReadString(environment, "executionProvider"), "CPU", StringComparison.Ordinal))
            {
                return Reject("Runtime or execution provider differs from the requested campaign.");
            }

            return new MemoryCampaignResumeValidation(true, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Reject($"Run manifest is incomplete or invalid: {exception.Message}");
        }
    }

    /// <summary>驗證完整 run directory 的 actual counts 與每個 raw artifact hash。</summary>
    public static MemoryCampaignResumeValidation ValidateRunDirectory(
        string runDirectory,
        MemoryCampaignIdentity expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        var manifestPath = Path.Combine(runDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return Reject("Run manifest is missing; the existing directory cannot be resumed.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var identityResult = Validate(document.RootElement, expected);
            if (!identityResult.IsReusable)
            {
                return identityResult;
            }

            var planned = document.RootElement.GetProperty("plannedCounts");
            var actual = document.RootElement.GetProperty("actualCounts");
            foreach (var counter in new[] { "warmupRequests", "attemptedRequests" })
            {
                if (planned.GetProperty(counter).GetInt32() != actual.GetProperty(counter).GetInt32())
                {
                    return Reject($"Run actual count '{counter}' does not match its planned count.");
                }
            }

            if (actual.GetProperty("completedRequests").GetInt32() + actual.GetProperty("errors").GetInt32() !=
                actual.GetProperty("attemptedRequests").GetInt32())
            {
                return Reject("Run actual request outcomes do not account for every attempted request.");
            }

            var artifacts = document.RootElement.GetProperty("artifacts").EnumerateArray().ToArray();
            if (artifacts.Length == 0)
            {
                return Reject("Run manifest contains no verified raw artifacts.");
            }

            var fullRoot = Path.GetFullPath(runDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var artifact in artifacts)
            {
                var relativePath = ReadString(artifact, "path");
                var path = Path.GetFullPath(Path.Combine(runDirectory, relativePath));
                if (!path.StartsWith(fullRoot, StringComparison.Ordinal) || !File.Exists(path))
                {
                    return Reject($"Run artifact '{relativePath}' is missing or escapes the run directory.");
                }

                if (new FileInfo(path).Length != artifact.GetProperty("sizeBytes").GetInt64() ||
                    !string.Equals(HashFile(path), ReadString(artifact, "sha256"), StringComparison.Ordinal))
                {
                    return Reject($"Run artifact '{relativePath}' size or SHA-256 does not match the manifest.");
                }
            }

            return new MemoryCampaignResumeValidation(true, null);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Reject($"Run evidence cannot be verified: {exception.Message}");
        }
    }

    /// <summary>取得必要非空 string property。</summary>
    private static string ReadString(JsonElement element, string name)
    {
        return element.GetProperty(name).GetString()
            ?? throw new InvalidDataException($"Manifest property '{name}' is null.");
    }

    /// <summary>讀取 string 或 null property。</summary>
    private static string? ReadOptionalString(JsonElement element, string name)
    {
        var property = element.GetProperty(name);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
    }

    /// <summary>計算 raw artifact SHA-256。</summary>
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>建立不可 reuse result 與原因。</summary>
    private static MemoryCampaignResumeValidation Reject(string reason)
    {
        return new MemoryCampaignResumeValidation(false, reason);
    }
}
