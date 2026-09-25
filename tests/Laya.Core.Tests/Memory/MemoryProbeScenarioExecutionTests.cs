extern alias MemoryProbe;

using Laya.Core.Configuration;
using MemoryProbe::Laya.MemoryProbe.Configuration;
using MemoryProbe::Laya.MemoryProbe.Scenarios;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "MultilingualModel")]
public sealed class MemoryProbeScenarioExecutionTests
{
    /// <summary>驗證元件隔離 scenario 在執行時不建立 inference session。</summary>
    [Theory]
    [InlineData("tokenizer-only", "tokenizer-zh")]
    [InlineData("sequence-only", "phase3a-short-transaction")]
    [InlineData("tensor-only", "phase3a-short-transaction")]
    [InlineData("calibration-only", "calibration-mixed-english-reference")]
    public void Create_ComponentScenariosDoNotOpenInferenceSession(string scenario, string workloadId)
    {
        var repositoryRoot = GetRepositoryRoot();
        var modelRoot = Path.Combine(repositoryRoot, "models", "laya-multilingual");
        var resolution = MemoryProbe::Laya.Shared.Configuration.LayaProfileResolver.Resolve(
            null,
            "multilingual",
            null,
            repositoryRoot);
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", scenario,
            "--requests", "1",
            "--workload", workloadId,
            "--output", Path.Combine(Path.GetTempPath(), "phase3a-test")
        ]);
        using var catalog = MemoryWorkloadCatalog.Load(
            Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads.json"),
            Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads-manifest.json"));
        using var execution = MemoryProbeScenarioExecution.Create(options, resolution, catalog);

        Assert.False(execution.HasInferenceSession);
        Assert.True(execution.Execute(0) >= TimeSpan.Zero);
        Assert.False(execution.HasInferenceSession);
        Assert.True(Directory.Exists(modelRoot));
    }

    /// <summary>驗證 run-only 跨多次 native Run 重用同一 inference session 與固定 input tensors。</summary>
    [Fact]
    public void RunOnly_ReusesSessionAndPreallocatedInputs()
    {
        var repositoryRoot = GetRepositoryRoot();
        var resolution = MemoryProbe::Laya.Shared.Configuration.LayaProfileResolver.Resolve(
            null,
            "multilingual",
            null,
            repositoryRoot);
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", "run-only",
            "--requests", "2",
            "--profile", "multilingual",
            "--workload", "phase3a-short-transaction"
        ]);
        using var catalog = MemoryWorkloadCatalog.Load(
            Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads.json"),
            Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads-manifest.json"));
        using var execution = MemoryProbeScenarioExecution.Create(options, resolution, catalog);

        Assert.True(execution.HasInferenceSession);
        Assert.True(execution.Execute(0) > TimeSpan.Zero);
        Assert.True(execution.Execute(1) > TimeSpan.Zero);
        Assert.True(execution.LastInferenceDuration > TimeSpan.Zero);
        Assert.True(execution.LastSequenceLength > 0);
    }

    /// <summary>驗證 full-pipeline 重用一個長生命週期 decision engine。</summary>
    [Fact]
    public void FullPipeline_ReusesLongLivedDecisionEngine()
    {
        var (execution, catalog) = CreateExecution("full-pipeline", "phase3a-short-transaction", 2);
        using var catalogLifetime = catalog;
        using var executionLifetime = execution;

        Assert.True(executionLifetime.HasInferenceSession);
        Assert.True(executionLifetime.Execute(0) > TimeSpan.Zero);
        Assert.True(executionLifetime.Execute(1) > TimeSpan.Zero);
        Assert.True(executionLifetime.LastInferenceDuration > TimeSpan.Zero);
        Assert.True(executionLifetime.LastSequenceLength > 0);
        Assert.False(executionLifetime.LastWasTruncated);
    }

    /// <summary>驗證 execution sample 將同一 request 的 sequence 與 latency 一起回傳。</summary>
    [Fact]
    public void ExecuteSample_ReturnsRequestLocalInferenceMetadata()
    {
        var (execution, catalog) = CreateExecution("full-pipeline", "phase3a-short-transaction", 1);
        using var catalogLifetime = catalog;
        using var executionLifetime = execution;

        var sample = executionLifetime.ExecuteSample(0);

        Assert.True(sample.EndToEndDuration > TimeSpan.Zero);
        Assert.True(sample.InferenceDuration > TimeSpan.Zero);
        Assert.True(sample.SequenceLength > 0);
        Assert.False(sample.WasTruncated);
    }

    /// <summary>驗證 session-recreate 每次 operation 結束後不保留 inference session。</summary>
    [Fact]
    public void SessionRecreate_DisposesEachSessionAfterOneRun()
    {
        var (execution, catalog) = CreateExecution("session-recreate", "phase3a-short-transaction", 2);
        using var catalogLifetime = catalog;
        using var executionLifetime = execution;

        Assert.False(executionLifetime.HasInferenceSession);
        Assert.True(executionLifetime.Execute(0) > TimeSpan.Zero);
        Assert.False(executionLifetime.HasInferenceSession);
        Assert.True(executionLifetime.Execute(1) > TimeSpan.Zero);
        Assert.False(executionLifetime.HasInferenceSession);
        Assert.True(executionLifetime.LastInferenceDuration > TimeSpan.Zero);
    }

    /// <summary>驗證每個 session recreate cycle 都在建立前與 Dispose 後回報 memory snapshot。</summary>
    [Fact]
    public void SessionRecreate_ReportsMemoryBeforeAndAfterEachCycle()
    {
        var (execution, catalog) = CreateExecution("session-recreate", "phase3a-short-transaction", 2);
        using var catalogLifetime = catalog;
        using var executionLifetime = execution;
        var samples = new List<(int RequestIndex, string Phase)>();

        _ = executionLifetime.Execute(0, (index, phase, snapshot) =>
        {
            Assert.True(snapshot.WorkingSetBytes > 0);
            samples.Add((index, phase));
        });
        _ = executionLifetime.Execute(1, (index, phase, snapshot) =>
        {
            Assert.True(snapshot.WorkingSetBytes > 0);
            samples.Add((index, phase));
        });

        Assert.Equal(
            new[] { (0, "before-session-create"), (0, "after-session-dispose"), (1, "before-session-create"), (1, "after-session-dispose") },
            samples);
    }

    /// <summary>驗證同一 full-pipeline session 依 schedule 執行 short、long、short state。</summary>
    [Fact]
    public void FullPipeline_StateScheduleChangesShapeWithoutChangingSession()
    {
        var repositoryRoot = GetRepositoryRoot();
        var resolution = MemoryProbe::Laya.Shared.Configuration.LayaProfileResolver.Resolve(
            null,
            "multilingual",
            null,
            repositoryRoot);
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", "full-pipeline",
            "--requests", "3",
            "--state-schedule", "short:1,long:1,short:1"
        ]);
        using var catalog = MemoryWorkloadCatalog.Load(
            Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads.json"),
            Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads-manifest.json"));
        using var execution = MemoryProbeScenarioExecution.Create(options, resolution, catalog);

        Assert.True(execution.HasInferenceSession);
        Assert.True(execution.IsStateScheduled);
        _ = execution.Execute(0);
        var firstShortLength = execution.LastSequenceLength;
        _ = execution.Execute(1);
        var longLength = execution.LastSequenceLength;
        _ = execution.Execute(2);

        Assert.True(execution.HasInferenceSession);
        Assert.True(longLength > firstShortLength);
        Assert.Equal(firstShortLength, execution.LastSequenceLength);
    }

    /// <summary>以真實 verified bundle 建立指定 scenario execution。</summary>
    private static (MemoryProbeScenarioExecution Execution, MemoryWorkloadCatalog Catalog) CreateExecution(
        string scenario,
        string workloadId,
        int requests)
    {
        var repositoryRoot = GetRepositoryRoot();
        var resolution = MemoryProbe::Laya.Shared.Configuration.LayaProfileResolver.Resolve(
            null,
            "multilingual",
            null,
            repositoryRoot);
        var options = MemoryProbeCommandLine.Parse(
        [
            "--scenario", scenario,
            "--requests", requests.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--profile", "multilingual",
            "--workload", workloadId
        ]);
        var catalog = MemoryWorkloadCatalog.Load(
            Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads.json"),
            Path.Combine(repositoryRoot, "test-data", "phase3a", "memory-workloads-manifest.json"));
        try
        {
            var execution = MemoryProbeScenarioExecution.Create(options, resolution, catalog);
            return (execution, catalog);
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
    }

    /// <summary>取得測試執行目錄所屬的 repository 根目錄。</summary>
    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }
}
