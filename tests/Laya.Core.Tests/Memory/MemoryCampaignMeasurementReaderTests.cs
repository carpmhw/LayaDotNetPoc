extern alias MemoryBenchmarks;

using MemoryBenchmarks::Laya.MemoryBenchmarks.Analysis;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryCampaignMeasurementReaderTests
{
    /// <summary>驗證 replicate metric 由 raw samples 重算 peak、end 與 natural late slopes。</summary>
    [Fact]
    public void ReadRunMetrics_ExcludesForcedGcFromSlopeAndKeepsPeak()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phase3a-metrics-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path,
            "sample_index,request_count,phase,private_memory_bytes,vmrss_bytes\n" +
            "0,0,measurement,1000,800\n" +
            "1,2500,measurement,3500,3300\n" +
            "2,5000,measurement,6000,5800\n" +
            "3,5000,forced-gc,900000,800000\n" +
            "4,5000,lifecycle,5500,5300\n");

        try
        {
            var metrics = MemoryCampaignMeasurementReader.ReadRunMetrics("run-test-001", path);

            Assert.Equal(900000, metrics.PeakPrivateMemoryBytes);
            Assert.Equal(5500, metrics.EndPrivateMemoryBytes);
            Assert.Equal(100, metrics.LatePrivateMemorySlopeBytesPer100Requests, precision: 6);
            Assert.Equal(800000, metrics.PeakRssBytes);
            Assert.Equal(5300, metrics.EndRssBytes);
            Assert.Equal(100, metrics.LateRssSlopeBytesPer100Requests, precision: 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>驗證缺 exact natural endpoint 時不製造 replicate slope。</summary>
    [Fact]
    public void ReadRunMetrics_RejectsMissingLateSegmentEndpoint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phase3a-metrics-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path,
            "sample_index,request_count,phase,private_memory_bytes,vmrss_bytes\n" +
            "0,0,measurement,1000,800\n" +
            "1,2500,measurement,3500,3300\n");

        try
        {
            Assert.Throws<InvalidDataException>(() => MemoryCampaignMeasurementReader.ReadRunMetrics("run-test-002", path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
