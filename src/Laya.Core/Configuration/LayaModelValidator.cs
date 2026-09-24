using Laya.Core.Exceptions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Laya.Core.Configuration;

/// <summary>驗證本機 Laya model bundle 的檔案與設定契約。</summary>
public static class LayaModelValidator
{
    /// <summary>驗證必要檔案並載入 laya_config.json。</summary>
    public static LayaModelBundle ValidateBundle(string modelRoot)
    {
        return ValidateBundle(modelRoot, manifestPath: null, options: null);
    }

    /// <summary>驗證必要檔案並套用指定 bundle manifest。</summary>
    public static LayaModelBundle ValidateBundle(string modelRoot, string? manifestPath)
    {
        return ValidateBundle(modelRoot, manifestPath, options: null);
    }

    /// <summary>驗證必要檔案、manifest provenance 與 host 傳入的模型身分。</summary>
    public static LayaModelBundle ValidateBundle(
        string modelRoot,
        string? manifestPath,
        LayaOptions? options)
    {
        var root = Path.GetFullPath(modelRoot);
        if (options is not null && !string.Equals(Path.TrimEndingDirectorySeparator(root),
            Path.TrimEndingDirectorySeparator(options.ModelRoot), PathComparison))
            throw new LayaConfigurationException("Requested model root does not match validation root.", root);
        var requiredFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["laya.onnx"] = Path.Combine(root, "laya.onnx"),
            ["laya_config.json"] = Path.Combine(root, "laya_config.json"),
            ["tokenizer/tokenizer.json"] = Path.Combine(root, "tokenizer", "tokenizer.json"),
            ["tokenizer/tokenizer_config.json"] = Path.Combine(root, "tokenizer", "tokenizer_config.json")
        };

        foreach (var name in requiredFiles.Keys) ResolveAssetPath(root, name);

        var missingFiles = requiredFiles
            .Where(item => !File.Exists(item.Value))
            .Select(item => item.Key)
            .ToArray();

        if (missingFiles.Length > 0)
        {
            throw new LayaModelNotFoundException(root, missingFiles);
        }

        var resolvedManifestPath = ResolveManifestPath(root, manifestPath);
        var manifest = resolvedManifestPath is null
            ? null
            : LoadManifest(resolvedManifestPath, root);
        if (manifest is null && options?.Profile == "multilingual")
            throw new LayaConfigurationException("Multilingual bundle requires a verified schemaVersion=2 manifest.", root);
        if (manifest is not null)
        {
            ValidateManifestIdentity(manifest, options, root);
        }

        IReadOnlyList<string> externalDataFiles;
        try
        {
            externalDataFiles = OnnxExternalDataReader.Read(requiredFiles["laya.onnx"]);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            throw new LayaConfigurationException($"Could not read ONNX graph: {exception.Message}",
                requiredFiles["laya.onnx"], innerException: exception);
        }
        var externalDataPaths = externalDataFiles.Select(path => ResolveAssetPath(root, path)).ToArray();
        var missingShards = externalDataFiles.Where((_, index) => !File.Exists(externalDataPaths[index])).ToArray();
        if (missingShards.Length > 0) throw new LayaModelNotFoundException(root, missingShards);
        if (manifest is not null) ValidateExternalDataFiles(manifest, externalDataFiles, root);

        var verifiedFiles = manifest is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ValidateManifestFiles(manifest, requiredFiles.Keys.Concat(externalDataFiles), root);
        var config = LayaModelConfig.Load(requiredFiles["laya_config.json"]);

        return new LayaModelBundle(
            root,
            requiredFiles["laya.onnx"],
            externalDataPaths.FirstOrDefault(),
            requiredFiles["laya_config.json"],
            requiredFiles["tokenizer/tokenizer.json"],
            requiredFiles["tokenizer/tokenizer_config.json"],
            config)
        {
            ExternalDataPaths = externalDataPaths,
            ManifestPath = resolvedManifestPath,
            Profile = manifest?.Profile,
            CheckpointRevision = manifest?.EffectiveRevision,
            VerifiedFiles = verifiedFiles
        };
    }

    /// <summary>解析明確 manifest 或 bundle 內的預設 manifest 路徑。</summary>
    private static string? ResolveManifestPath(string root, string? manifestPath)
    {
        if (manifestPath is not null)
        {
            var resolved = Path.GetFullPath(manifestPath, root);
            ResolveAssetPath(root, Path.GetRelativePath(root, resolved).Replace(Path.DirectorySeparatorChar, '/'));
            if (!File.Exists(resolved))
            {
                throw new LayaModelNotFoundException(resolved, new[] { "manifest" });
            }

            return resolved;
        }

        var defaultPath = ResolveAssetPath(root, "laya-bundle-manifest.json");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    /// <summary>讀取並驗證 manifest JSON 的基本結構。</summary>
    private static BundleManifest LoadManifest(string path, string root)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            ValidateUniqueProperties(document.RootElement, root);
            var manifest = document.RootElement.Deserialize<BundleManifest>();
            if (manifest is null || manifest.Files is null || manifest.Files.Count == 0 ||
                manifest.ExternalDataFiles is null)
            {
                throw new LayaConfigurationException(
                    $"Bundle manifest '{path}' requires nonempty files and an externalDataFiles array (which may be empty).",
                    root);
            }

            return manifest;
        }
        catch (LayaException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LayaConfigurationException(
                $"Could not read bundle manifest '{path}'.",
                root,
                innerException: exception);
        }
    }

    /// <summary>遞迴拒絕重複 JSON key，避免不同消費端採用不同的身分或 hash。</summary>
    private static void ValidateUniqueProperties(JsonElement element, string root)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new LayaConfigurationException($"Duplicate manifest property '{property.Name}'.", root);
                ValidateUniqueProperties(property.Value, root);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateUniqueProperties(item, root);
    }

    /// <summary>驗證 manifest profile、checkpoint 與 tokenizer provenance。</summary>
    private static void ValidateManifestIdentity(
        BundleManifest manifest,
        LayaOptions? options,
        string root)
    {
        if (manifest.SchemaVersion is not (null or 1 or 2))
            throw new LayaConfigurationException("Unsupported bundle manifest schemaVersion.", root);
        if ((options?.Profile == "multilingual" || manifest.Profile == "multilingual") &&
            (manifest.SchemaVersion != 2 ||
             (manifest.Status != "verified" &&
              !(options?.AllowCandidateStaged == true && manifest.Status == "candidate-staged"))))
            throw new LayaConfigurationException("Multilingual bundle requires a verified schemaVersion=2 manifest.", root);
        if (manifest.SchemaVersion == 2 && (string.IsNullOrWhiteSpace(manifest.Profile) ||
            string.IsNullOrWhiteSpace(manifest.Checkpoint?.Revision) ||
            string.IsNullOrWhiteSpace(manifest.Checkpoint?.Repository)))
            throw new LayaConfigurationException("Bundle v2 requires profile and checkpoint repository/revision.", root);
        if (manifest.Profile == "multilingual" &&
            manifest.Checkpoint?.Repository != "convaiinnovations/laya-multilingual")
            throw new LayaConfigurationException("Multilingual profile does not match checkpoint repository.", root);
        if (manifest.CheckpointRevision is not null && manifest.Checkpoint?.Revision is not null &&
            !string.Equals(manifest.CheckpointRevision, manifest.Checkpoint.Revision, StringComparison.Ordinal))
            throw new LayaConfigurationException("Bundle checkpoint revisions conflict.", root);

        if (options?.Profile is not null &&
            !string.Equals(options.Profile, manifest.Profile, StringComparison.Ordinal))
        {
            throw new LayaConfigurationException(
                $"Bundle profile '{manifest.Profile}' does not match requested profile '{options.Profile}'.",
                root);
        }

        if (options?.CheckpointRevision is not null &&
            !string.Equals(options.CheckpointRevision, manifest.EffectiveRevision, StringComparison.Ordinal))
        {
            throw new LayaConfigurationException(
                $"Bundle checkpoint '{manifest.EffectiveRevision}' does not match requested checkpoint '" +
                $"{options.CheckpointRevision}'.",
                root);
        }

        if (manifest.TokenizerCheckpointRevision is not null &&
            !string.Equals(
                manifest.TokenizerCheckpointRevision,
                manifest.EffectiveRevision,
                StringComparison.Ordinal))
        {
            throw new LayaConfigurationException(
                "Bundle tokenizer checkpoint identity does not match the model checkpoint.",
                root);
        }
    }

    /// <summary>逐檔驗證宣告路徑，並要求 external-data 與 graph 引用集合完全一致。</summary>
    private static void ValidateExternalDataFiles(BundleManifest manifest, IReadOnlyList<string> references, string root)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in manifest.ExternalDataFiles)
        {
            var path = ResolveAssetPath(root, name);
            if (!File.Exists(path)) throw new LayaModelNotFoundException(root, new[] { name });
            if (!declared.Add(name))
                throw new LayaConfigurationException($"Duplicate external-data file '{name}'.", root);
        }
        if (!declared.SetEquals(references))
            throw new LayaConfigurationException(
                $"Manifest external-data files do not match ONNX references. Missing: {string.Join(", ", references.Except(declared))}; " +
                $"extra: {string.Join(", ", declared.Except(references))}.", root);
    }

    /// <summary>驗證 manifest 每個檔案的 size 與 SHA-256，並回傳實際 hash。</summary>
    private static IReadOnlyDictionary<string, string> ValidateManifestFiles(
        BundleManifest manifest,
        IEnumerable<string> requiredFiles,
        string root)
    {
        foreach (var name in requiredFiles)
            if (!manifest.Files.ContainsKey(name))
                throw new LayaConfigurationException($"Manifest omits required asset '{name}'.", root);
        var verified = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in manifest.Files)
        {
            var path = ResolveAssetPath(root, item.Key);
            if (item.Value is null || item.Value.Size is null or < 0 ||
                item.Value.Sha256 is not { Length: 64 } || !item.Value.Sha256.All(Uri.IsHexDigit))
            {
                throw new LayaConfigurationException(
                    $"Manifest file '{item.Key}' requires a nonnegative size and SHA-256.",
                    root);
            }

            if (!File.Exists(path))
            {
                throw new LayaModelNotFoundException(root, new[] { item.Key });
            }

            using var stream = File.OpenRead(path);
            if (stream.Length != item.Value.Size)
            {
                throw new LayaConfigurationException(
                    $"Manifest size mismatch for '{item.Key}': expected {item.Value.Size}, actual {stream.Length}.",
                    path);
            }

            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualHash, item.Value.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LayaConfigurationException(
                    $"Manifest SHA-256 mismatch for '{item.Key}'.",
                    path);
            }

            verified[item.Key] = actualHash;
        }

        return verified;
    }

    /// <summary>依作業系統選取實體路徑的大小寫比較規則。</summary>
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>只接受明確 POSIX 相對路徑；bundle 內任何符號連結或 junction 均拒絕以封鎖逃逸。</summary>
    private static string ResolveAssetPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') || relativePath.Contains(':') || relativePath.Any(char.IsControl) ||
            relativePath.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
            throw new LayaConfigurationException($"Unsafe bundle asset path '{relativePath}'.", root);
        var current = root;
        foreach (var part in relativePath.Split('/'))
        {
            current = Path.Combine(current, part);
            var info = new FileInfo(current);
            if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0) ||
                (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new LayaConfigurationException($"Bundle asset '{relativePath}' contains a symbolic link or reparse point.", root);
        }
        return current;
    }

    /// <summary>保存 manifest 內單一檔案的可驗證 metadata。</summary>
    private sealed class ManifestFile
    {
        /// <summary>取得檔案大小。</summary>
        [JsonPropertyName("size")]
        public long? Size { get; init; }

        /// <summary>取得檔案 SHA-256。</summary>
        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;
    }

    /// <summary>保存 bundle manifest 的必要 provenance 欄位。</summary>
    private sealed class BundleManifest
    {
        /// <summary>取得 manifest schema 版本，未宣告者限用 English legacy 路徑。</summary>
        [JsonPropertyName("schemaVersion")]
        public int? SchemaVersion { get; init; }

        /// <summary>取得 publication status，runtime 只接受 verified bundle。</summary>
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        /// <summary>取得 v2 巢狀 checkpoint 身分。</summary>
        [JsonPropertyName("checkpoint")]
        public ManifestCheckpoint? Checkpoint { get; init; }

        /// <summary>取得版本對應的 checkpoint revision。</summary>
        [JsonIgnore]
        public string? EffectiveRevision => Checkpoint?.Revision ?? CheckpointRevision;
        /// <summary>取得模型 profile。</summary>
        [JsonPropertyName("profile")]
        public string? Profile { get; init; }

        /// <summary>取得模型 checkpoint revision。</summary>
        [JsonPropertyName("checkpointRevision")]
        public string? CheckpointRevision { get; init; }

        /// <summary>取得 tokenizer 對應的 checkpoint revision。</summary>
        [JsonPropertyName("tokenizerCheckpointRevision")]
        public string? TokenizerCheckpointRevision { get; init; }

        /// <summary>取得每個 bundle 檔案的 hash metadata。</summary>
        [JsonPropertyName("files")]
        public Dictionary<string, ManifestFile> Files { get; init; } =
            new(StringComparer.Ordinal);

        /// <summary>取得 ONNX external-data shard 相對路徑。</summary>
        [JsonPropertyName("externalDataFiles")]
        public List<string> ExternalDataFiles { get; init; } = null!;
    }

    /// <summary>保存 v2 的模型來源與固定 revision。</summary>
    private sealed class ManifestCheckpoint
    {
        /// <summary>取得 checkpoint 的來源 repository。</summary>
        [JsonPropertyName("repository")]
        public string? Repository { get; init; }

        /// <summary>取得 checkpoint 的固定 revision。</summary>
        [JsonPropertyName("revision")]
        public string? Revision { get; init; }
    }
}
