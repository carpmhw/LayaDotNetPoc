extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Orchestration;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryCampaignCommandLineTests
{
    /// <summary>驗證 pilot campaign CLI 不需要 formal policy 且使用明確 campaign identity。</summary>
    [Fact]
    public void Parse_AcceptsPilotCampaignOptions()
    {
        var options = MemoryCampaignCommandLine.Parse(
        [
            "--stage", "baseline",
            "--campaign-id", "phase3a-pilot-20260924",
            "--mode", "pilot",
            "--output", "reports/phase3a"
        ]);

        Assert.Equal("baseline", options.Stage);
        Assert.Equal("phase3a-pilot-20260924", options.CampaignId);
        Assert.Equal("pilot", options.Mode);
        Assert.False(options.Resume);
    }

    /// <summary>驗證 formal campaign 必須載入 schema-valid frozen policy。</summary>
    [Fact]
    public void Parse_RejectsFormalCampaignWithoutPolicy()
    {
        Assert.Throws<ArgumentException>(() => MemoryCampaignCommandLine.Parse(
        [
            "--stage", "baseline",
            "--campaign-id", "phase3a-formal-20260924",
            "--mode", "formal"
        ]));
    }

    /// <summary>驗證未知、重複、空 campaign ID 參數會在啟動 child process 前拒絕。</summary>
    [Theory]
    [MemberData(nameof(InvalidCampaignArgumentSets))]
    public void Parse_RejectsInvalidOrUnknownCampaignArguments(string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => MemoryCampaignCommandLine.Parse(arguments));
    }

    /// <summary>提供不合法 campaign ID 與未知 option 的 CLI 測試資料。</summary>
    public static TheoryData<string[]> InvalidCampaignArgumentSets => new()
    {
        { new[] { "--stage", "baseline", "--campaign-id", "a", "--mode", "pilot" } },
        { new[] { "--stage", "baseline", "--campaign-id", "phase3a-run", "--mode", "pilot", "--oops" } }
    };
}
