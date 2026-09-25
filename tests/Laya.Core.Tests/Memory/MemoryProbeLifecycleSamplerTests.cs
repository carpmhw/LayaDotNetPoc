extern alias MemoryProbe;

using MemoryProbe::Laya.MemoryProbe.Telemetry;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryProbeLifecycleSamplerTests
{
    /// <summary>驗證 T0／T1／T2、warmup 與正式 request=0 checkpoint 保持不同階段。</summary>
    [Fact]
    public void CaptureLifecycle_PreservesWarmupAndMeasuredRequestCounts()
    {
        var samples = new List<MemoryProbeSample>();
        var sampler = new MemoryProbeLifecycleSampler(samples.Add);

        sampler.CaptureT0BeforeModelLoad();
        sampler.CaptureT1AfterModelLoad();
        sampler.CaptureT2AfterFirstOperation(8, inferenceDuration: TimeSpan.FromMilliseconds(2));
        sampler.CaptureWarmupComplete(warmupRequests: 5, totalOperationCount: 5);
        sampler.CaptureRequestCheckpoint(
            attemptedRequests: 0,
            completedRequests: 0,
            errors: 0,
            integrityFailures: 0,
            warmupRequests: 5,
            totalOperationCount: 5);

        Assert.Equal(
            new[] { "t0-before-model-load", "t1-after-model-load", "t2-after-first-operation", "warmup-complete", "request-count-0" },
            samples.Select(sample => sample.Reason));
        Assert.All(samples.Take(3), sample => Assert.Equal(0, sample.WarmupRequests));
        Assert.All(samples.Skip(3), sample => Assert.Equal(5, sample.WarmupRequests));
        Assert.Equal(0, samples[^1].RequestCount);
        Assert.Equal(5, samples[^1].TotalOperationCount);
        Assert.Equal(8, samples[2].SequenceLength);
        Assert.Equal(TimeSpan.FromMilliseconds(2), samples[2].LastInferenceDuration);
    }

    /// <summary>驗證 request checkpoint 同時保留 attempted、completed、error 與 integrity 計數。</summary>
    [Fact]
    public void CaptureRequestCheckpoint_PreservesActualCompletedCounts()
    {
        MemoryProbeSample? sample = null;
        var sampler = new MemoryProbeLifecycleSampler(value => sample = value);
        sampler.CaptureT0BeforeModelLoad();
        sampler.CaptureRequestCheckpoint(
            attemptedRequests: 100,
            completedRequests: 97,
            errors: 2,
            integrityFailures: 1,
            warmupRequests: 5,
            totalOperationCount: 105);

        Assert.NotNull(sample);
        Assert.Equal("request-count-97", sample.Reason);
        Assert.Equal(100, sample.AttemptedRequests);
        Assert.Equal(97, sample.CompletedRequests);
        Assert.Equal(2, sample.Errors);
        Assert.Equal(1, sample.IntegrityFailures);
        Assert.Equal(105, sample.TotalOperationCount);
    }
}
