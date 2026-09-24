using System.Security.Cryptography;
using System.Text.Json;

namespace Laya.Shared.Configuration;

/// <summary>描述一個可供 host 選擇的模型 profile。</summary>
public sealed record LayaProfileDefinition(
    string Name,
    string ModelRoot,
    string? CheckpointRevision);

/// <summary>保存設定來源提供的 profile 名稱與 root mapping。</summary>
public sealed record LayaProfileSettings(
    string? Profile,
    IReadOnlyDictionary<string, LayaProfileDefinition> Profiles);

/// <summary>保存 host 完成優先序解析後的模型身分。</summary>
public sealed record LayaProfileResolution(
    string Name,
    string ModelRoot,
    string? CheckpointRevision,
    string Source);

/// <summary>在 Console、Benchmark 與驗收入口共用模型 profile 解析規則。</summary>
public static class LayaProfileResolver
{
    private static readonly IReadOnlyDictionary<string, LayaProfileDefinition> HostDefaults =
        new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)
        {
            ["english"] = new(
                "english",
                Path.Combine("models", "laya"),
                "68f27dfe5a27a54fb2b1fefc432f43f972e90868"),
            ["multilingual"] = new(
                "multilingual",
                Path.Combine("models", "laya-multilingual"),
                "052592a15d198d9ad47da779604259b10b47b7aa")
        };

    /// <summary>建立沒有設定 profile 的預設設定，保留 English host fallback。</summary>
    public static LayaProfileSettings CreateDefaultSettings()
    {
        return new LayaProfileSettings(
            null,
            new Dictionary<string, LayaProfileDefinition>(HostDefaults, StringComparer.Ordinal));
    }

    /// <summary>從環境變數讀取 Laya.Profile 與各 profile 的 root mapping。</summary>
    public static LayaProfileSettings LoadEnvironment()
    {
        var settings = CreateDefaultSettings();
        var profiles = new Dictionary<string, LayaProfileDefinition>(settings.Profiles, StringComparer.Ordinal);

        foreach (var name in profiles.Keys.ToArray())
        {
            var value = Environment.GetEnvironmentVariable($"Laya.Profiles.{name}.ModelRoot")
                ?? Environment.GetEnvironmentVariable($"LAYA_PROFILES_{name.ToUpperInvariant()}_MODEL_ROOT")
                ?? (name == "multilingual" ? Environment.GetEnvironmentVariable("LAYA_MULTILINGUAL_MODEL_ROOT") : null)
                ?? (name == "english" ? Environment.GetEnvironmentVariable("LAYA_MODEL_ROOT") : null);
            if (!string.IsNullOrWhiteSpace(value))
            {
                var current = profiles[name];
                profiles[name] = current with { ModelRoot = value };
            }
        }

        return new LayaProfileSettings(
            Environment.GetEnvironmentVariable("Laya.Profile")
                ?? Environment.GetEnvironmentVariable("LAYA_PROFILE"),
            profiles);
    }

    /// <summary>從簡單 appsettings JSON 讀取 Laya.Profile 與 Laya.Profiles mapping。</summary>
    public static LayaProfileSettings LoadJson(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Laya profile configuration was not found: {path}", path);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var laya = document.RootElement.TryGetProperty("Laya", out var layaElement)
                ? layaElement
                : document.RootElement;
            var configuredProfile = ReadOptionalString(laya, "Profile");
            var profiles = new Dictionary<string, LayaProfileDefinition>(
                CreateDefaultSettings().Profiles,
                StringComparer.Ordinal);

            if (laya.TryGetProperty("Profiles", out var profileElement) &&
                profileElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var profile in profileElement.EnumerateObject())
                {
                    if (profile.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidOperationException(
                            $"Laya profile '{profile.Name}' must be a JSON object.");
                    }

                    var modelRoot = ReadOptionalString(profile.Value, "ModelRoot");
                    if (string.IsNullOrWhiteSpace(modelRoot))
                    {
                        throw new InvalidOperationException(
                            $"Laya profile '{profile.Name}' is missing ModelRoot.");
                    }

                    var checkpoint = ReadOptionalString(profile.Value, "CheckpointRevision");
                    profiles[profile.Name] = new LayaProfileDefinition(
                        profile.Name,
                        modelRoot,
                        checkpoint ?? profiles.GetValueOrDefault(profile.Name)?.CheckpointRevision);
                }
            }

            return new LayaProfileSettings(configuredProfile, profiles);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Laya profile configuration '{path}' is invalid JSON.",
                exception);
        }
    }

    /// <summary>依 root override、CLI profile、設定 profile、English fallback 的順序解析。</summary>
    public static LayaProfileResolution Resolve(
        string? modelRootOverride,
        string? cliProfile,
        LayaProfileSettings? settings = null,
        string? workingDirectory = null)
    {
        var effectiveSettings = settings ?? CreateDefaultSettings();
        var profiles = new Dictionary<string, LayaProfileDefinition>(HostDefaults, StringComparer.Ordinal);
        foreach (var item in effectiveSettings.Profiles)
        {
            profiles[item.Key] = item.Value;
        }

        var selectedProfile = cliProfile ?? effectiveSettings.Profile;
        if (selectedProfile is not null && string.IsNullOrWhiteSpace(selectedProfile))
        {
            throw new ArgumentException("Laya profile cannot be empty.", nameof(cliProfile));
        }

        var baseDirectory = Path.GetFullPath(workingDirectory ?? Directory.GetCurrentDirectory());
        if (modelRootOverride is not null)
        {
            if (string.IsNullOrWhiteSpace(modelRootOverride))
            {
                throw new ArgumentException("Model root cannot be empty.", nameof(modelRootOverride));
            }

            var root = Path.GetFullPath(modelRootOverride, baseDirectory);
            var definition = selectedProfile is not null && profiles.TryGetValue(selectedProfile, out var selected)
                ? selected
                : null;
            if (selectedProfile is not null && definition is null)
            {
                throw new ArgumentException($"Unknown Laya profile '{selectedProfile}'.", nameof(cliProfile));
            }

            if (definition is not null)
            {
                var publishedRoot = ResolvePublishedRoot(root, baseDirectory, definition);
                ValidateRootIdentity(publishedRoot, definition);
                return new LayaProfileResolution(
                    definition.Name,
                    publishedRoot,
                    definition.CheckpointRevision,
                    "model-root");
            }

            var identity = ReadRootIdentity(root);
            if (identity is null)
            {
                return new LayaProfileResolution("custom", root, null, "model-root");
            }

            if (!profiles.TryGetValue(identity.Value.Profile, out var identifiedProfile))
            {
                throw new InvalidOperationException(
                    $"Model root '{root}' identifies unknown profile '{identity.Value.Profile}'.");
            }

            ValidateRootIdentity(root, identifiedProfile);
            return new LayaProfileResolution(
                identifiedProfile.Name,
                root,
                identity.Value.Revision ?? identifiedProfile.CheckpointRevision,
                "model-root");
        }

        var profileName = string.IsNullOrWhiteSpace(selectedProfile) ? "english" : selectedProfile;
        if (!profiles.TryGetValue(profileName, out var profileDefinition))
        {
            throw new ArgumentException($"Unknown Laya profile '{profileName}'.", nameof(cliProfile));
        }

        var source = cliProfile is not null ? "cli-profile"
            : effectiveSettings.Profile is not null ? "configured-profile"
            : "host-default";
        var resolvedRoot = Path.GetFullPath(profileDefinition.ModelRoot, baseDirectory);
        resolvedRoot = ResolvePublishedRoot(resolvedRoot, baseDirectory, profileDefinition);
        ValidateRootIdentity(resolvedRoot, profileDefinition);
        return new LayaProfileResolution(
            profileDefinition.Name,
            resolvedRoot,
            profileDefinition.CheckpointRevision,
            source);
    }

    /// <summary>解析 multilingual current pointer，確保 default root 指向已驗證版本。</summary>
    private static string ResolvePublishedRoot(
        string root,
        string baseDirectory,
        LayaProfileDefinition definition)
    {
        if (definition.Name != "multilingual" ||
            File.Exists(Path.Combine(root, "laya-bundle-manifest.json")))
        {
            return root;
        }

        var pointerPath = Path.Combine(root, "current-bundle.json");
        if (!File.Exists(pointerPath))
        {
            return root;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(pointerPath));
            var pointer = document.RootElement;
            if (pointer.GetProperty("schemaVersion").GetInt32() != 1 ||
                pointer.GetProperty("status").GetString() != "verified" ||
                pointer.GetProperty("profile").GetString() != definition.Name)
            {
                throw new InvalidOperationException(
                    $"Current multilingual bundle pointer '{pointerPath}' is not verified.");
            }

            var versionRoot = Path.GetFullPath(pointer.GetProperty("root").GetString()!, baseDirectory);
            var containerRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!versionRoot.StartsWith(containerRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Current multilingual bundle pointer '{pointerPath}' escapes its model root.");
            }

            var manifestPath = Path.Combine(versionRoot, "laya-bundle-manifest.json");
            var pointedManifest = Path.GetFullPath(pointer.GetProperty("manifest").GetString()!, baseDirectory);
            if (!string.Equals(pointedManifest, manifestPath, StringComparison.Ordinal) || !File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    $"Current multilingual bundle pointer '{pointerPath}' has an invalid manifest path.");
            }

            using var stream = File.OpenRead(manifestPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(
                    actualHash,
                    pointer.GetProperty("verifiedManifestSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Current multilingual bundle pointer '{pointerPath}' has a manifest hash mismatch.");
            }

            return versionRoot;
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"Current multilingual bundle pointer '{pointerPath}' is incomplete.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Current multilingual bundle pointer '{pointerPath}' is invalid JSON.",
                exception);
        }
    }

    /// <summary>從 root manifest 驗證明確 profile 的 checkpoint 身分。</summary>
    private static void ValidateRootIdentity(string root, LayaProfileDefinition definition)
    {
        var manifestPath = new[]
        {
            Path.Combine(root, "laya-bundle-manifest.json"),
            Path.Combine(root, "model-manifest.json"),
            Path.Combine(root, "bundle-manifest.json")
        }.FirstOrDefault(File.Exists);

        if (manifestPath is null)
        {
            if (definition.Name == "multilingual" && Directory.Exists(root))
            {
                throw new InvalidOperationException(
                    $"Multilingual model root '{root}' requires a schemaVersion=2 bundle manifest.");
            }

            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var rootElement = document.RootElement;
            var schemaVersion = rootElement.TryGetProperty("schemaVersion", out var schema)
                && schema.ValueKind == JsonValueKind.Number
                ? schema.GetInt32()
                : (int?)null;
            var actualProfile = ReadOptionalString(rootElement, "profile");
            var actualRevision = ReadOptionalString(rootElement, "checkpointRevision");
            if (actualRevision is null && rootElement.TryGetProperty("checkpoint", out var checkpoint) &&
                checkpoint.ValueKind == JsonValueKind.Object)
            {
                actualRevision = ReadOptionalString(checkpoint, "revision");
            }
            if (definition.Name == "multilingual" && (schemaVersion != 2 || actualProfile is null || actualRevision is null))
            {
                throw new InvalidOperationException(
                    $"Multilingual model root '{root}' requires profile, checkpoint revision, and schemaVersion=2.");
            }

            if (actualProfile is not null && !string.Equals(actualProfile, definition.Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Model root '{root}' identifies profile '{actualProfile}', expected '{definition.Name}'.");
            }

            if (definition.CheckpointRevision is not null && actualRevision is not null &&
                !string.Equals(actualRevision, definition.CheckpointRevision, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Model root '{root}' identifies checkpoint '{actualRevision}', expected '{definition.CheckpointRevision}'.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Model identity manifest '{manifestPath}' is invalid JSON.",
                exception);
        }
    }

    /// <summary>讀取 root manifest 的 profile 與 nested/legacy revision，供 root-only 解析使用。</summary>
    private static (string Profile, string? Revision)? ReadRootIdentity(string root)
    {
        var manifestPath = new[]
        {
            Path.Combine(root, "laya-bundle-manifest.json"),
            Path.Combine(root, "model-manifest.json"),
            Path.Combine(root, "bundle-manifest.json")
        }.FirstOrDefault(File.Exists);

        if (manifestPath is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var element = document.RootElement;
            var profile = ReadOptionalString(element, "profile");
            if (profile is null)
            {
                return null;
            }

            var revision = ReadOptionalString(element, "checkpointRevision");
            if (revision is null && element.TryGetProperty("checkpoint", out var checkpoint) &&
                checkpoint.ValueKind == JsonValueKind.Object)
            {
                revision = ReadOptionalString(checkpoint, "revision");
            }

            return (profile, revision);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Model identity manifest '{manifestPath}' is invalid JSON.",
                exception);
        }
    }

    /// <summary>讀取可選的 JSON 字串欄位，拒絕非字串值。</summary>
    private static string? ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Laya configuration property '{name}' must be a string.");
        }

        return property.GetString();
    }
}
