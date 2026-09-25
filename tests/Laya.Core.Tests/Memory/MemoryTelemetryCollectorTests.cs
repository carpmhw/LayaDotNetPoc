extern alias MemoryProbe;

using MemoryProbe::Laya.MemoryProbe.Telemetry;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryTelemetryCollectorTests
{
    /// <summary>驗證 Linux proc status 的 kB 欄位會精確換算成 bytes。</summary>
    [Fact]
    public void ParseLinuxStatus_ConvertsKilobytesToBytes()
    {
        const string status = "VmRSS:\t17 kB\nVmSize:\t81 kB\nVmData:\t23 kB\nRssAnon:\t11 kB\nRssFile:\t5 kB\nRssShmem:\t1 kB\n";

        var snapshot = LinuxProcStatusReader.Parse(status);

        Assert.Equal(17 * 1024, snapshot.VmRss.Bytes);
        Assert.Equal(81 * 1024, snapshot.VmSize.Bytes);
        Assert.Equal(23 * 1024, snapshot.VmData.Bytes);
        Assert.Equal(11 * 1024, snapshot.RssAnon.Bytes);
        Assert.Equal(5 * 1024, snapshot.RssFile.Bytes);
        Assert.Equal(1024, snapshot.RssShmem.Bytes);
    }

    /// <summary>驗證缺少或損壞的 proc 欄位保留 null 與可診斷原因。</summary>
    [Fact]
    public void ParseLinuxStatus_ReportsMissingAndInvalidFields()
    {
        var snapshot = LinuxProcStatusReader.Parse("VmRSS: not-a-number kB\nVmSize: 1 MB\n");

        Assert.Null(snapshot.VmRss.Bytes);
        Assert.Contains("invalid", snapshot.VmRss.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(snapshot.VmSize.Bytes);
        Assert.Contains("unit", snapshot.VmSize.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(snapshot.VmData.Bytes);
        Assert.Contains("missing", snapshot.VmData.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證無法讀取 proc status 時所有 Linux 指標都標記為不可用。</summary>
    [Fact]
    public void ReadLinuxStatus_ReportsUnavailablePathInsteadOfZero()
    {
        var snapshot = LinuxProcStatusReader.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Null(snapshot.VmRss.Bytes);
        Assert.Contains("unavailable", snapshot.VmRss.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(snapshot.RssAnon.Bytes);
    }

    /// <summary>驗證 process 與 GC collector 擷取非負 bytes、handles、threads 與 counts。</summary>
    [Fact]
    public void Capture_ReportsProcessAndManagedGcCounters()
    {
        var snapshot = ProcessMemoryCollector.Capture();

        Assert.True(snapshot.WorkingSetBytes > 0);
        Assert.True(snapshot.PrivateMemoryBytes > 0);
        Assert.True(snapshot.VirtualMemoryBytes > 0);
        Assert.True(snapshot.ThreadCount > 0);
        Assert.True(snapshot.HandleCount >= 0);
        if (snapshot.ManagedHeap.Bytes is { } managedHeapBytes)
        {
            Assert.True(managedHeapBytes >= 0);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(snapshot.ManagedHeap.UnavailableReason));
        }
        Assert.True(snapshot.FragmentedBytes >= 0);
        Assert.True(snapshot.TotalAllocatedBytes >= 0);
        Assert.True(snapshot.Generation0Count >= 0);
        Assert.True(snapshot.Generation1Count >= 0);
        Assert.True(snapshot.Generation2Count >= 0);
    }

    /// <summary>驗證 managed heap counter 負值以 null 與原因保存，不會被當成實際 heap bytes。</summary>
    [Fact]
    public void FromReportedBytes_RejectsNegativeManagedHeapCounter()
    {
        var metric = MemoryMetricValue.FromReportedBytes(-3_189_608, "GC.GetTotalMemory(false)");

        Assert.Null(metric.Bytes);
        Assert.Contains("negative", metric.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GC.GetTotalMemory(false)", metric.UnavailableReason!, StringComparison.Ordinal);
    }

    /// <summary>驗證 managed heap counter 的零值與正值仍保留原 bytes 數值。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    public void FromReportedBytes_PreservesNonNegativeManagedHeapCounter(long bytes)
    {
        var metric = MemoryMetricValue.FromReportedBytes(bytes, "GC.GetTotalMemory(false)");

        Assert.Equal(bytes, metric.Bytes);
        Assert.Null(metric.UnavailableReason);
    }

    /// <summary>驗證可用 memory metric 不接受負 bytes。</summary>
    [Fact]
    public void Available_RejectsNegativeBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryMetricValue.Available(-1));
    }
}
