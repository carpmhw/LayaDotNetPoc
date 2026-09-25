namespace Laya.MemoryProbe.Telemetry;

/// <summary>執行明確標記的 forced-GC 與 post-soak idle observation。</summary>
internal static class MemoryProbeDiagnostics
{
    /// <summary>擷取 GC 前後 checkpoint 並執行完整 Collect／Wait／Collect sequence。</summary>
    public static void ForceFullGc(Action<string> captureCheckpoint)
    {
        ArgumentNullException.ThrowIfNull(captureCheckpoint);
        captureCheckpoint("forced-gc-before");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        captureCheckpoint("forced-gc-after");
    }

    /// <summary>依指定 idle 秒數等待並於每個 interval 結束後採樣。</summary>
    public static async ValueTask ObserveIdleAsync(
        IReadOnlyList<int> idleSeconds,
        Func<TimeSpan, CancellationToken, ValueTask> wait,
        Action<int> captureCheckpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idleSeconds);
        ArgumentNullException.ThrowIfNull(wait);
        ArgumentNullException.ThrowIfNull(captureCheckpoint);
        if (idleSeconds.Any(seconds => seconds < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(idleSeconds), "Idle wait durations cannot be negative.");
        }

        foreach (var seconds in idleSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await wait(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
            captureCheckpoint(seconds);
        }
    }
}
