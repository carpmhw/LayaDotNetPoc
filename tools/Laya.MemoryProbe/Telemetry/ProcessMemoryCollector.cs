using System.Diagnostics;

namespace Laya.MemoryProbe.Telemetry;

/// <summary>擷取 process、Linux proc 與 .NET GC counters，不執行 forced GC。</summary>
internal static class ProcessMemoryCollector
{
    /// <summary>Refresh process counters 並擷取一次 bytes-based memory snapshot。</summary>
    public static ProcessMemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var gcInfo = GC.GetGCMemoryInfo();
        var linux = OperatingSystem.IsLinux()
            ? LinuxProcStatusReader.Read("/proc/self/status")
            : LinuxProcStatusSnapshot.Unavailable("Linux /proc counters are unavailable on this operating system.");

        return new ProcessMemorySnapshot(
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.VirtualMemorySize64,
            process.HandleCount,
            process.Threads.Count,
            MemoryMetricValue.FromReportedBytes(
                GC.GetTotalMemory(forceFullCollection: false),
                "GC.GetTotalMemory(false)"),
            gcInfo.HeapSizeBytes,
            gcInfo.FragmentedBytes,
            gcInfo.Index,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            GC.GetTotalAllocatedBytes(precise: false),
            linux);
    }
}
