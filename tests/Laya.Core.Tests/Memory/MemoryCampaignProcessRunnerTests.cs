extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Orchestration;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryCampaignProcessRunnerTests
{
    /// <summary>驗證 child process start info 使用獨立 dotnet run 與完整 Probe CLI arguments。</summary>
    [Fact]
    public void CreateStartInfo_BuildsIsolatedMultilingualProbeCommand()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var invocation = MemoryCampaignPlanner.CreatePlan("baseline", "campaign-test-001", "pilot").Single();

        var startInfo = MemoryCampaignProcessRunner.CreateStartInfo(
            repositoryRoot,
            invocation,
            Path.Combine(Path.GetTempPath(), "phase3a-campaign-output"),
            modelRoot: null,
            policyPath: null);

        var arguments = startInfo.ArgumentList.ToArray();
        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal(repositoryRoot, startInfo.WorkingDirectory);
        Assert.Contains("--project", arguments);
        Assert.Contains("tools/Laya.MemoryProbe/Laya.MemoryProbe.csproj", arguments);
        Assert.Contains("--scenario", arguments);
        Assert.Contains("full-pipeline", arguments);
        Assert.Contains("--profile", arguments);
        Assert.Contains("multilingual", arguments);
        Assert.Contains("--run-id", arguments);
        Assert.Contains(invocation.RunId, arguments);
    }

    /// <summary>驗證 formal child process 不可省略 policy path。</summary>
    [Fact]
    public void CreateStartInfo_RejectsFormalInvocationWithoutPolicy()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var invocation = MemoryCampaignPlanner.CreatePlan("baseline", "campaign-test-002", "formal").Single();

        Assert.Throws<ArgumentException>(() => MemoryCampaignProcessRunner.CreateStartInfo(
            repositoryRoot,
            invocation,
            Path.Combine(Path.GetTempPath(), "phase3a-campaign-output"),
            modelRoot: null,
            policyPath: null));
    }
}
