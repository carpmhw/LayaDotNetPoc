using Laya.Shared.Configuration;
using System.Security.Cryptography;
using System.Text.Json;

namespace Laya.Core.Tests.Configuration;

[Trait("Category", "PureLogic")]
public sealed class LayaProfileResolverTests
{
    /// <summary>驗證明確 model root 優先於 CLI、設定與 host 預設。</summary>
    [Fact]
    public void Resolve_UsesExplicitRootBeforeOtherSources()
    {
        var settings = new LayaProfileSettings(
            "multilingual",
            new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)
            {
                ["english"] = new("english", "configured-english", "english-revision"),
                ["multilingual"] = new("multilingual", "configured-multilingual", "multi-revision")
            });

        var resolution = LayaProfileResolver.Resolve(
            "override-model",
            "multilingual",
            settings,
            "/tmp/laya-profile-test");

        Assert.Equal("multilingual", resolution.Name);
        Assert.Equal(Path.GetFullPath("override-model", "/tmp/laya-profile-test"), resolution.ModelRoot);
        Assert.Equal("model-root", resolution.Source);
    }

    /// <summary>驗證 CLI profile 優先於設定 profile 並使用其 mapping。</summary>
    [Fact]
    public void Resolve_UsesCliProfileBeforeConfiguredProfile()
    {
        var settings = new LayaProfileSettings(
            "multilingual",
            new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)
            {
                ["english"] = new("english", "english-root", "english-revision"),
                ["multilingual"] = new("multilingual", "multi-root", "multi-revision")
            });

        var resolution = LayaProfileResolver.Resolve(null, "english", settings, "/tmp/laya-profile-test");

        Assert.Equal("english", resolution.Name);
        Assert.Equal(Path.GetFullPath("english-root", "/tmp/laya-profile-test"), resolution.ModelRoot);
        Assert.Equal("cli-profile", resolution.Source);
    }

    /// <summary>驗證沒有 CLI 指定時會使用設定 profile。</summary>
    [Fact]
    public void Resolve_UsesConfiguredProfileBeforeHostDefault()
    {
        var settings = new LayaProfileSettings(
            "multilingual",
            new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)
            {
                ["english"] = new("english", "english-root", "english-revision"),
                ["multilingual"] = new("multilingual", "multi-root", "multi-revision")
            });

        var resolution = LayaProfileResolver.Resolve(null, null, settings, "/tmp/laya-profile-test");

        Assert.Equal("multilingual", resolution.Name);
        Assert.Equal("configured-profile", resolution.Source);
    }

    /// <summary>驗證沒有任何覆寫時維持 English host 預設。</summary>
    [Fact]
    public void Resolve_UsesEnglishHostDefault()
    {
        var resolution = LayaProfileResolver.Resolve(
            null,
            null,
            new LayaProfileSettings(null, new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)),
            "/tmp/laya-profile-test");

        Assert.Equal("english", resolution.Name);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("models", "laya"), "/tmp/laya-profile-test"),
            resolution.ModelRoot);
        Assert.Equal("host-default", resolution.Source);
    }

    /// <summary>驗證未知 profile、缺少 profile 值與 mapping 缺值會立即拒絕。</summary>
    [Theory]
    [InlineData("unknown", null)]
    [InlineData("", null)]
    [InlineData(null, "")]
    public void Resolve_RejectsInvalidProfile(string? profile, string? configuredProfile)
    {
        var settings = new LayaProfileSettings(
            configuredProfile,
            new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal));

        Assert.Throws<ArgumentException>(() =>
            LayaProfileResolver.Resolve(null, profile, settings, "/tmp/laya-profile-test"));
    }

    /// <summary>驗證 profile root 的相對路徑以啟動目錄解析。</summary>
    [Fact]
    public void Resolve_ResolvesRelativeConfiguredRootAgainstWorkingDirectory()
    {
        var settings = new LayaProfileSettings(
            "english",
            new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)
            {
                ["english"] = new("english", "relative-model", "revision")
            });

        var resolution = LayaProfileResolver.Resolve(null, null, settings, "/tmp/laya-profile-test");

        Assert.Equal(
            Path.GetFullPath("relative-model", "/tmp/laya-profile-test"),
            resolution.ModelRoot);
    }

    /// <summary>驗證 root manifest 的 profile 身分與明確 profile 不一致時拒絕啟動。</summary>
    [Fact]
    public void Resolve_RejectsMismatchedRootManifestIdentity()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "laya-bundle-manifest.json"),
                "{\"profile\":\"english\",\"checkpointRevision\":\"english-revision\"}");
            var settings = new LayaProfileSettings(
                null,
                new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)
                {
                    ["multilingual"] = new("multilingual", "multi-root", "multi-revision")
                });

            Assert.Throws<InvalidOperationException>(() =>
                LayaProfileResolver.Resolve(directory.FullName, "multilingual", settings));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證 v2 nested checkpoint revision mismatch 不可由 root-only profile 繞過。</summary>
    [Fact]
    public void Resolve_RejectsNestedCheckpointRevisionMismatch()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "laya-bundle-manifest.json"),
                "{\"schemaVersion\":2,\"profile\":\"multilingual\",\"checkpoint\":{\"repository\":\"convaiinnovations/laya-multilingual\",\"revision\":\"wrong\"}} ");
            var settings = LayaProfileResolver.CreateDefaultSettings();

            Assert.Throws<InvalidOperationException>(() =>
                LayaProfileResolver.Resolve(directory.FullName, null, settings));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證 root-only v2 bundle 會從 manifest 推導 multilingual 身分與 revision。</summary>
    [Fact]
    public void Resolve_DerivesIdentityFromNestedV2Manifest()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "laya-bundle-manifest.json"),
                "{\"schemaVersion\":2,\"profile\":\"multilingual\",\"checkpoint\":{\"repository\":\"convaiinnovations/laya-multilingual\",\"revision\":\"052592a15d198d9ad47da779604259b10b47b7aa\"}} ");

            var resolution = LayaProfileResolver.Resolve(
                directory.FullName,
                null,
                LayaProfileResolver.CreateDefaultSettings());

            Assert.Equal("multilingual", resolution.Name);
            Assert.Equal(
                "052592a15d198d9ad47da779604259b10b47b7aa",
                resolution.CheckpointRevision);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證已存在的 multilingual root 沒有 v2 manifest 時 fail closed。</summary>
    [Fact]
    public void Resolve_RejectsExistingMultilingualRootWithoutManifest()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var settings = new LayaProfileSettings(
                null,
                new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)
                {
                    ["multilingual"] = new("multilingual", directory.FullName, "multi-revision")
                });

            Assert.Throws<InvalidOperationException>(() =>
                LayaProfileResolver.Resolve(null, "multilingual", settings));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證 multilingual container root 會驗證 current pointer 並解析 versioned bundle。</summary>
    [Fact]
    public void Resolve_FollowsVerifiedMultilingualCurrentPointer()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var versionRoot = Directory.CreateDirectory(Path.Combine(directory.FullName, "versions", "v1"));
            var manifestPath = Path.Combine(versionRoot.FullName, "laya-bundle-manifest.json");
            File.WriteAllText(manifestPath,
                "{\"schemaVersion\":2,\"profile\":\"multilingual\",\"checkpoint\":{\"revision\":\"052592a15d198d9ad47da779604259b10b47b7aa\"}}");
            using var stream = File.OpenRead(manifestPath);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var relativeVersionRoot = Path.GetRelativePath(Directory.GetCurrentDirectory(), versionRoot.FullName)
                .Replace(Path.DirectorySeparatorChar, '/');
            var relativeManifestPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), manifestPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            File.WriteAllText(
                Path.Combine(directory.FullName, "current-bundle.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "verified",
                    profile = "multilingual",
                    root = relativeVersionRoot,
                    manifest = relativeManifestPath,
                    verifiedManifestSha256 = hash
                }));

            var settings = new LayaProfileSettings(
                null,
                new Dictionary<string, LayaProfileDefinition>(StringComparer.Ordinal)
                {
                    ["multilingual"] = new("multilingual", directory.FullName, "052592a15d198d9ad47da779604259b10b47b7aa")
                });
            var resolution = LayaProfileResolver.Resolve(null, "multilingual", settings);

            Assert.Equal(versionRoot.FullName, resolution.ModelRoot);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }
}
