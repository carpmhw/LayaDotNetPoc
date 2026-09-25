namespace Laya.MemoryProbe.Telemetry;

/// <summary>保存一次 process、GC 與可選 Linux proc memory snapshot。</summary>
internal sealed class ProcessMemorySnapshot
{
    /// <summary>建立 bytes-based process 與 managed GC snapshot。</summary>
    public ProcessMemorySnapshot(
        long workingSetBytes,
        long privateMemoryBytes,
        long virtualMemoryBytes,
        int handleCount,
        int threadCount,
        MemoryMetricValue managedHeap,
        long heapSizeBytes,
        long fragmentedBytes,
        long gcIndex,
        int generation0Count,
        int generation1Count,
        int generation2Count,
        long totalAllocatedBytes,
        LinuxProcStatusSnapshot linux)
    {
        WorkingSetBytes = workingSetBytes;
        PrivateMemoryBytes = privateMemoryBytes;
        VirtualMemoryBytes = virtualMemoryBytes;
        HandleCount = handleCount;
        ThreadCount = threadCount;
        ManagedHeap = managedHeap;
        HeapSizeBytes = heapSizeBytes;
        FragmentedBytes = fragmentedBytes;
        GcIndex = gcIndex;
        Generation0Count = generation0Count;
        Generation1Count = generation1Count;
        Generation2Count = generation2Count;
        TotalAllocatedBytes = totalAllocatedBytes;
        Linux = linux;
    }

    /// <summary>取得 process working set bytes。</summary>
    public long WorkingSetBytes { get; }

    /// <summary>取得 process private memory bytes。</summary>
    public long PrivateMemoryBytes { get; }

    /// <summary>取得 process virtual memory bytes。</summary>
    public long VirtualMemoryBytes { get; }

    /// <summary>取得 process handle count。</summary>
    public int HandleCount { get; }

    /// <summary>取得 process thread count。</summary>
    public int ThreadCount { get; }

    /// <summary>取得不強制 GC 的 managed heap bytes 與不可用原因。</summary>
    public MemoryMetricValue ManagedHeap { get; }

    /// <summary>取得最近一次 GC 記錄的 heap size bytes。</summary>
    public long HeapSizeBytes { get; }

    /// <summary>取得最近一次 GC 記錄的 fragmented bytes。</summary>
    public long FragmentedBytes { get; }

    /// <summary>取得最近一次 GC snapshot index；零表示尚無已完成 GC snapshot。</summary>
    public long GcIndex { get; }

    /// <summary>取得 Gen0 collection count。</summary>
    public int Generation0Count { get; }

    /// <summary>取得 Gen1 collection count。</summary>
    public int Generation1Count { get; }

    /// <summary>取得 Gen2 collection count。</summary>
    public int Generation2Count { get; }

    /// <summary>取得 process-wide total allocated bytes。</summary>
    public long TotalAllocatedBytes { get; }

    /// <summary>取得 Linux proc metrics；非 Linux 平台會標示 unavailable。</summary>
    public LinuxProcStatusSnapshot Linux { get; }
}
