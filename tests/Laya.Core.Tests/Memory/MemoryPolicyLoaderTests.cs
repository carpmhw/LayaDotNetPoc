extern alias MemoryBenchmarks;

using System.Text.Json;
using System.Text.Json.Nodes;
using MemoryBenchmarks::Laya.MemoryBenchmarks.Analysis;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryPolicyLoaderTests
{
    /// <summary>驗證 frozen policy 載入 threshold、target、minimum matrix 與原始 hash。</summary>
    [Fact]
    public void Load_AcceptsCompleteFrozenPolicy()
    {
        var path = WritePolicy(CreateValidPolicy());
        try
        {
            var policy = MemoryPolicyLoader.Load(path);

            Assert.Equal("phase3a-policy-v1", policy.PolicyId);
            Assert.Equal(3_000_000_000L, policy.TargetMemoryLimitBytes);
            Assert.Equal(128_000_000, policy.PrivateMemory.LateGrowthBudgetBytes);
            Assert.Equal(5000, policy.MinimumMatrix.BaselineRequests);
            Assert.Matches("^[a-f0-9]{64}$", policy.Sha256);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>驗證 policy 缺少 calibration threshold 或 frozen provenance 時拒絕載入。</summary>
    [Theory]
    [InlineData("status")]
    [InlineData("pilotRunIds")]
    [InlineData("thresholds")]
    [InlineData("minimumMatrix")]
    public void Load_RejectsIncompletePolicy(string propertyToRemove)
    {
        var document = JsonNode.Parse(CreateValidPolicy())!.AsObject();
        document.Remove(propertyToRemove);
        var path = WritePolicy(document.ToJsonString());

        try
        {
            Assert.Throws<InvalidDataException>(() => MemoryPolicyLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>驗證指定的三十億 bytes deployment target 不得被 policy 改寫。</summary>
    [Fact]
    public void Load_RejectsWrongTargetMemoryLimit()
    {
        var document = JsonNode.Parse(CreateValidPolicy())!.AsObject();
        document["targetMemoryLimitBytes"] = 4_000_000_000L;
        var path = WritePolicy(document.ToJsonString());

        try
        {
            Assert.Throws<InvalidDataException>(() => MemoryPolicyLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>建立 schema 完整且所有 threshold 有明確數值的 frozen policy。</summary>
    internal static string CreateValidPolicy()
    {
        var slopeBudget = new { maximumLateSlopeBytesPer100Requests = 1_000_000d, lateGrowthBudgetBytes = 128_000_000 };
        var document = new
        {
            schemaVersion = 1,
            policyId = "phase3a-policy-v1",
            status = "frozen",
            frozenUtc = "2026-09-24T13:00:00Z",
            freezeCommit = "98f4840c2b7746b94825fe904f9228ed63c43392",
            pilotRunIds = new[] { "pilot-run-001" },
            pilotArtifactSha256 = new string('a', 64),
            targetMemoryLimitBytes = 3_000_000_000L,
            rationale = "Frozen from a low-overhead pilot and the selected deployment budget.",
            limitations = "POC reference only; not a production SLA.",
            thresholds = new
            {
                privateMemory = slopeBudget,
                rss = slopeBudget,
                managedHeap = slopeBudget,
                nearLinearR2Minimum = 0.95,
                replicateTolerance = new
                {
                    peakRelativeDifferenceMaximum = 0.10,
                    endRelativeDifferenceMaximum = 0.10,
                    slopeRelativeDifferenceMaximum = 0.20
                }
            },
            minimumMatrix = new
            {
                baselineRuns = 2,
                runOnlyRuns = 2,
                arenaOnRuns = 2,
                arenaOffRuns = 2,
                dockerTargetRuns = 2,
                repetitionsBeforeThirdRun = 2,
                baselineRequests = 5000,
                runOnlyRequests = 5000,
                arenaRequests = 5000,
                tokenizerRequestsPerLanguage = 10000,
                sequenceOnlyRequests = 10000,
                tensorOnlyRequests = 10000,
                calibrationOnlyRequests = 10000,
                shapeAndConcurrencyRequests = 1000,
                dockerLimitsBytes = new long[] { 2_000_000_000, 2_500_000_000, 3_000_000_000, 4_000_000_000 },
                dockerSmokeRequests = new[] { 100, 1000 },
                dockerExtendedLimitsBytes = new long[] { 3_000_000_000, 4_000_000_000 },
                dockerExtendedRequests = 5000,
                sessionRecreateCycles = new[] { 10, 100 },
                shapeProfiles = new[] { "short", "medium", "long" },
                questionCounts = new[] { 1, 2, 5 },
                concurrencyLevels = new[] { 1, 2, 4 }
            }
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>將 policy JSON 寫入隔離 temporary path。</summary>
    private static string WritePolicy(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"phase3a-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, contents);
        return path;
    }
}
