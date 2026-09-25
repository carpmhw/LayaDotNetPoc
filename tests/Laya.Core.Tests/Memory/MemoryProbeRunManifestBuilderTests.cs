extern alias MemoryProbe;

using System.Security.Cryptography;
using System.Text.Json;
using MemoryProbe::Laya.MemoryProbe.Configuration;
using MemoryProbe::Laya.MemoryProbe.Reporting;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "MultilingualModel")]
public sealed class MemoryProbeRunManifestBuilderTests
{
    /// <summary>驗證 run manifest 綁定 fixture、bundle、policy-null 與實際 requested options。</summary>
    [Fact]
    public void Build_RecordsVerifiedModelAndWorkloadIdentity()
    {
        var repositoryRoot = GetRepositoryRoot();
        var profile = MemoryProbe::Laya.Shared.Configuration.LayaProfileResolver.Resolve(
            null,
            "multilingual",
            null,
            repositoryRoot);
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", "full-pipeline",
            "--requests", "5000",
            "--run-id", "manifest-test-001"
        ]);
        var workloadManifest = Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads-manifest.json");
        var environment = new Dictionary<string, object?>
        {
            ["os"] = "Linux",
            ["kernel"] = "test-kernel",
            ["architecture"] = "x64",
            ["cpu"] = "test-cpu",
            ["logicalCores"] = 8,
            ["ramBytes"] = 16_000_000_000L,
            ["swapBytes"] = 2_000_000_000L,
            ["dotnetSdk"] = "10.0.112",
            ["dotnetRuntime"] = ".NET 10.0.12",
            ["onnxRuntime"] = "1.30.0",
            ["executionProvider"] = "CPU",
            ["containerRuntime"] = null,
            ["memoryLimitBytes"] = null,
            ["memorySwapLimitBytes"] = null
        };
        var sourceIdentity = new Dictionary<string, object?>
        {
            ["commit"] = "98f4840c2b7746b94825fe904f9228ed63c43392",
            ["dirty"] = true,
            ["dirtyIdentity"] = "aabbccdd"
        };

        var manifest = MemoryProbeRunManifestBuilder.Build(
            options,
            profile,
            workloadManifest,
            sourceIdentity,
            environment,
            DateTimeOffset.Parse("2026-09-24T00:00:00Z"));

        Assert.Equal(1, manifest["schemaVersion"]);
        Assert.Equal("in-progress", manifest["status"]);
        Assert.Equal("multilingual", Assert.IsType<Dictionary<string, object?>>(manifest["model"])["profile"]);
        Assert.Null(manifest["policySha256"]);
        Assert.Equal(5000, Assert.IsType<Dictionary<string, object?>>(manifest["plannedCounts"])["attemptedRequests"]);

        var model = Assert.IsType<Dictionary<string, object?>>(manifest["model"]);
        Assert.Equal(ReadBundleManifestHash(profile.ModelRoot), model["bundleManifestSha256"]);
        Assert.Equal("609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f", model["tokenizerSha256"]);
        using var workload = JsonDocument.Parse(File.ReadAllText(workloadManifest));
        Assert.Equal(workload.RootElement.GetProperty("workload").GetProperty("sha256").GetString(), manifest["workloadSha256"]);
    }

    /// <summary>取得測試執行目錄所屬的 repository 根目錄。</summary>
    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    /// <summary>計算 versioned bundle manifest 的實際 SHA-256。</summary>
    private static string ReadBundleManifestHash(string modelRoot)
    {
        using var stream = File.OpenRead(Path.Combine(modelRoot, "laya-bundle-manifest.json"));
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
