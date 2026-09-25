extern alias MemoryBenchmarks;

using System.Text.Json;
using MemoryBenchmarks::Laya.MemoryBenchmarks.Orchestration;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryCampaignResumeValidatorTests
{
    /// <summary>驗證相同 complete source／policy／workload／bundle run 可供 resume reuse。</summary>
    [Fact]
    public void Validate_AcceptsCompleteRunWithMatchingIdentity()
    {
        var identity = CreateIdentity();
        using var manifest = JsonDocument.Parse(CreateManifest(identity, "complete"));

        var result = MemoryCampaignResumeValidator.Validate(manifest.RootElement, identity);

        Assert.True(result.IsReusable);
        Assert.Null(result.Reason);
    }

    /// <summary>驗證任何 source／policy／workload／bundle identity 漂移均拒絕 reuse。</summary>
    [Theory]
    [InlineData("commit")]
    [InlineData("dirtyIdentity")]
    [InlineData("policySha256")]
    [InlineData("workloadSha256")]
    [InlineData("bundleManifestSha256")]
    [InlineData("tokenizerSha256")]
    [InlineData("runtime")]
    public void Validate_RejectsIdentityMismatch(string changedIdentity)
    {
        var identity = CreateIdentity();
        using var manifest = JsonDocument.Parse(CreateManifest(identity, "complete", changedIdentity));

        var result = MemoryCampaignResumeValidator.Validate(manifest.RootElement, identity);

        Assert.False(result.IsReusable);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    /// <summary>驗證 incomplete 或 terminal-failure run 不可當作 resume-complete evidence。</summary>
    [Theory]
    [InlineData("in-progress")]
    [InlineData("incomplete")]
    [InlineData("terminal-failure")]
    public void Validate_RejectsNonCompleteRun(string status)
    {
        var identity = CreateIdentity();
        using var manifest = JsonDocument.Parse(CreateManifest(identity, status));

        var result = MemoryCampaignResumeValidator.Validate(manifest.RootElement, identity);

        Assert.False(result.IsReusable);
    }

    /// <summary>建立固定 resume identity 測試資料。</summary>
    private static MemoryCampaignIdentity CreateIdentity()
    {
        return new MemoryCampaignIdentity(
            "98f4840c2b7746b94825fe904f9228ed63c43392",
            true,
            "dirty-source-hash",
            "policy-hash",
            "workload-hash",
            "052592a15d198d9ad47da779604259b10b47b7aa",
            "bundle-hash",
            "tokenizer-hash",
            ".NET 10.0.12",
            "1.30.0");
    }

    /// <summary>建立包含可選 identity mismatch 的 machine-readable run manifest fixture。</summary>
    private static string CreateManifest(
        MemoryCampaignIdentity identity,
        string status,
        string? changedIdentity = null)
    {
        var commit = changedIdentity == "commit" ? "different-commit" : identity.SourceCommit;
        var dirtyIdentity = changedIdentity == "dirtyIdentity" ? "different-dirty-hash" : identity.DirtyIdentity;
        var policyHash = changedIdentity == "policySha256" ? "different-policy" : identity.PolicySha256;
        var workloadHash = changedIdentity == "workloadSha256" ? "different-workload" : identity.WorkloadSha256;
        var bundleHash = changedIdentity == "bundleManifestSha256" ? "different-bundle" : identity.BundleManifestSha256;
        var tokenizerHash = changedIdentity == "tokenizerSha256" ? "different-tokenizer" : identity.TokenizerSha256;
        var runtime = changedIdentity == "runtime" ? ".NET 8.0.0" : identity.DotNetRuntime;
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            runId = "resume-test-001",
            campaignId = "campaign-test-001",
            mode = "formal",
            status,
            scenario = "full-pipeline",
            startedUtc = "2026-09-24T00:00:00Z",
            endedUtc = "2026-09-24T00:05:00Z",
            sourceIdentity = new { commit, dirty = identity.SourceDirty, dirtyIdentity },
            policySha256 = policyHash,
            workloadSha256 = workloadHash,
            model = new
            {
                profile = "multilingual",
                revision = identity.ModelRevision,
                bundleRoot = "models/laya-multilingual/version",
                bundleManifestSha256 = bundleHash,
                tokenizerSha256 = tokenizerHash
            },
            environment = new
            {
                os = "Linux",
                kernel = "test-kernel",
                architecture = "x64",
                cpu = "test-cpu",
                logicalCores = 8,
                ramBytes = 16_000_000_000L,
                swapBytes = 2_000_000_000L,
                dotnetSdk = "10.0.112",
                dotnetRuntime = runtime,
                onnxRuntime = identity.OnnxRuntime,
                executionProvider = "CPU"
            },
            requestedConfiguration = new { profile = "multilingual", concurrency = 1, cpuArena = true, warmupRequests = 5, sampleEvery = 50 },
            plannedCounts = new { warmupRequests = 5, attemptedRequests = 5000, completedRequests = 5000, errors = 0, integrityFailures = 0 },
            actualCounts = new { warmupRequests = 5, attemptedRequests = 5000, completedRequests = 5000, errors = 0, integrityFailures = 0 },
            terminalOutcome = "completed",
            artifacts = Array.Empty<object>()
        });
    }
}
