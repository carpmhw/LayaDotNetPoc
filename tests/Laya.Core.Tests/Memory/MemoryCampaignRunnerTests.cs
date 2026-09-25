extern alias MemoryBenchmarks;

using System.Text.Json;
using MemoryBenchmarks::Laya.MemoryBenchmarks.Orchestration;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "MultilingualModel")]
public sealed class MemoryCampaignRunnerTests
{
    /// <summary>驗證 pilot campaign 以新 process 執行並可 resume 同一組完整 matching runs。</summary>
    [Fact]
    public async Task ExecuteAsync_RunsPilotChildrenAndResumesMatchingEvidence()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var outputRoot = Path.Combine(Path.GetTempPath(), $"phase3a-campaign-{Guid.NewGuid():N}");
        var campaignId = $"phase3a-pilot-{Guid.NewGuid():N}";
        try
        {
            var options = MemoryCampaignCommandLine.Parse(
            [
                "--stage", "pilot",
                "--campaign-id", campaignId,
                "--mode", "pilot",
                "--output", outputRoot
            ]);

            var firstExitCode = await MemoryCampaignRunner.ExecuteAsync(options, repositoryRoot);
            Assert.Equal(0, firstExitCode);
            var campaignDirectory = Path.Combine(outputRoot, "campaigns", campaignId, "pilot");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(campaignDirectory, "campaign.json")));
            var stageStatus = manifest.RootElement.GetProperty("stageStatus");
            Assert.Equal(4, stageStatus.EnumerateObject().Count());
            Assert.All(stageStatus.EnumerateObject(), stage => Assert.Equal("complete", stage.Value.GetString()));
            Assert.True(Directory.GetFiles(Path.Combine(campaignDirectory, "host-logs"), "*.stdout.log").Length >= 4);

            var resumeOptions = MemoryCampaignCommandLine.Parse(
            [
                "--stage", "pilot",
                "--campaign-id", campaignId,
                "--mode", "pilot",
                "--output", outputRoot,
                "--resume"
            ]);
            Assert.Equal(0, await MemoryCampaignRunner.ExecuteAsync(resumeOptions, repositoryRoot));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }
}
