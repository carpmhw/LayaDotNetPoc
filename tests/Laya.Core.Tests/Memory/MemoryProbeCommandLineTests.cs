extern alias MemoryProbe;

using MemoryProbe::Laya.MemoryProbe.Configuration;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryProbeCommandLineTests
{
    /// <summary>驗證完整 pipeline CLI 解析固定 workload 與 multilingual 預設值。</summary>
    [Fact]
    public void Parse_AcceptsFullPipelineConfiguration()
    {
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", "full-pipeline",
            "--requests", "5000",
            "--concurrency", "1",
            "--sample-every", "50",
            "--cpu-arena", "on",
            "--workload", "phase3a-short-transaction"
        ]);

        Assert.Equal(MemoryProbeScenario.FullPipeline, options.Scenario);
        Assert.Equal(5000, options.Requests);
        Assert.Equal(1, options.Concurrency);
        Assert.Equal(50, options.SampleEvery);
        Assert.True(options.CpuArenaEnabled);
        Assert.Equal("multilingual", options.ProfileName);
        Assert.Equal("phase3a-short-transaction", options.WorkloadId);
        Assert.Equal(5, options.WarmupRequests);
    }

    /// <summary>驗證非正 requests、concurrency 與採樣週期在初始化前拒絕。</summary>
    [Theory]
    [InlineData("--requests", "0")]
    [InlineData("--requests", "-1")]
    [InlineData("--concurrency", "0")]
    [InlineData("--sample-every", "0")]
    public void Parse_RejectsNonPositiveWorkloadValues(string option, string value)
    {
        var arguments = new List<string> { "--scenario", "full-pipeline", "--requests", "10", option, value };

        Assert.Throws<ArgumentException>(() => MemoryProbeCommandLine.Parse(arguments));
    }

    /// <summary>驗證 request count 上限避免 bounded latency windows 無界配置。</summary>
    [Fact]
    public void Parse_RejectsRequestsAboveBoundedWindowLimit()
    {
        Assert.Throws<ArgumentException>(() => MemoryProbeCommandLine.Parse(
        [
            "--scenario", "tokenizer-only",
            "--requests", "100001"
        ]));
    }

    /// <summary>驗證 session recreate 僅接受單一並行度且不執行 warmup。</summary>
    [Fact]
    public void Parse_RejectsConcurrentSessionRecreation()
    {
        Assert.Throws<ArgumentException>(() => MemoryProbeCommandLine.Parse(
        [
            "--scenario", "session-recreate",
            "--requests", "10",
            "--concurrency", "2"
        ]));
    }

    /// <summary>驗證 run-only 可依 worker 分別持有 inputs 與 RunOptions 並行執行。</summary>
    [Fact]
    public void Parse_AllowsConcurrentRunOnlyWorkers()
    {
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", "run-only",
            "--requests", "1000",
            "--concurrency", "2"
        ]);

        Assert.Equal(2, options.Concurrency);
    }

    /// <summary>驗證 state schedule 僅能取代明確固定 state 並須符合 requests 總數。</summary>
    [Fact]
    public void Parse_RejectsConflictingStateAndSchedule()
    {
        Assert.Throws<ArgumentException>(() => MemoryProbeCommandLine.Parse(
        [
            "--scenario", "full-pipeline",
            "--requests", "3000",
            "--state", "short",
            "--state-schedule", "short:1000,long:1000,short:1000"
        ]));
    }

    /// <summary>驗證 formal run 缺少 frozen policy 時會在模型載入前拒絕。</summary>
    [Fact]
    public void Parse_RejectsFormalModeWithoutPolicy()
    {
        Assert.Throws<ArgumentException>(() => MemoryProbeCommandLine.Parse(
        [
            "--scenario", "full-pipeline",
            "--requests", "5000",
            "--mode", "formal"
        ]));
    }

    /// <summary>驗證 formal run 省略 300 秒 idle 時必須記錄原因。</summary>
    [Fact]
    public void Parse_RequiresReasonWhenFormalIdleOmitsFiveMinutes()
    {
        var policyPath = Path.Combine(Path.GetTempPath(), $"phase3a-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyPath, MemoryPolicyLoaderTests.CreateValidPolicy());
        var arguments = new[]
        {
            "--scenario", "full-pipeline",
            "--requests", "5000",
            "--mode", "formal",
            "--policy", policyPath,
            "--idle-seconds", "30,60"
        };

        try
        {
            Assert.Throws<ArgumentException>(() => MemoryProbeCommandLine.Parse(arguments));
            var options = MemoryProbeCommandLine.Parse(arguments.Concat(
            [
                "--idle-omission-reason", "test host cannot reserve an additional five-minute window"
            ]).ToArray());

            Assert.Equal("test host cannot reserve an additional five-minute window", options.IdleOmissionReason);
        }
        finally
        {
            File.Delete(policyPath);
        }
    }

    /// <summary>驗證 formal Probe CLI 拒絕存在但未凍結或 schema 不完整的 policy。</summary>
    [Fact]
    public void Parse_RejectsUnfrozenFormalPolicy()
    {
        var policyPath = Path.Combine(Path.GetTempPath(), $"phase3a-unfrozen-{Guid.NewGuid():N}.json");
        File.WriteAllText(policyPath, "{\"schemaVersion\":1,\"status\":\"draft\"}");
        try
        {
            Assert.Throws<InvalidDataException>(() => MemoryProbeCommandLine.Parse(
            [
                "--scenario", "full-pipeline",
                "--requests", "5000",
                "--mode", "formal",
                "--policy", policyPath
            ]));
        }
        finally
        {
            File.Delete(policyPath);
        }
    }

    /// <summary>驗證 load-only 不會同時接受正式 scenario 與 request 工作量。</summary>
    [Fact]
    public void Parse_RejectsLoadOnlyWithScenario()
    {
        Assert.Throws<ArgumentException>(() => MemoryProbeCommandLine.Parse(
        [
            "--load-only",
            "--scenario", "full-pipeline",
            "--requests", "10"
        ]));
    }

    /// <summary>驗證 load-only 可解析 arena 組態且不要求 request 工作量。</summary>
    [Fact]
    public void Parse_AcceptsLoadOnlyWithoutRequestScenario()
    {
        var options = MemoryProbeCommandLine.Parse(
        [
            "--load-only",
            "--profile", "multilingual",
            "--cpu-arena", "off"
        ]);

        Assert.True(options.IsLoadOnly);
        Assert.Null(options.Scenario);
        Assert.Equal(0, options.Requests);
        Assert.False(options.CpuArenaEnabled);
    }

    /// <summary>驗證重複及未知 CLI 參數明確失敗。</summary>
    [Theory]
    [MemberData(nameof(InvalidArgumentSets))]
    public void Parse_RejectsDuplicateOrUnknownOptions(string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => MemoryProbeCommandLine.Parse(arguments));
    }

    /// <summary>提供重複參數與未知參數的 CLI 測試資料。</summary>
    public static TheoryData<string[]> InvalidArgumentSets => new()
    {
        { new[] { "--scenario", "full-pipeline", "--scenario", "run-only", "--requests", "10" } },
        { new[] { "--scenario", "full-pipeline", "--requests", "10", "--surprise", "value" } }
    };
}
