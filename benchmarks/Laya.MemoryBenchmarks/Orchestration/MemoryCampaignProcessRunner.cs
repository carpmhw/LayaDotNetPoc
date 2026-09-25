using System.Diagnostics;

namespace Laya.MemoryBenchmarks.Orchestration;

/// <summary>保存單一 Probe child process 的命令、退出碼與 raw host log paths。</summary>
internal sealed class MemoryCampaignChildResult
{
    /// <summary>建立 child process outcome descriptor。</summary>
    public MemoryCampaignChildResult(
        string runId,
        int exitCode,
        string standardOutputPath,
        string standardErrorPath)
    {
        RunId = runId;
        ExitCode = exitCode;
        StandardOutputPath = standardOutputPath;
        StandardErrorPath = standardErrorPath;
    }

    /// <summary>取得 Probe run ID。</summary>
    public string RunId { get; }

    /// <summary>取得 process exit code。</summary>
    public int ExitCode { get; }

    /// <summary>取得完整 standard output log 路徑。</summary>
    public string StandardOutputPath { get; }

    /// <summary>取得完整 standard error log 路徑。</summary>
    public string StandardErrorPath { get; }
}

/// <summary>以獨立 process 啟動 MemoryProbe 並保存 stdout／stderr／exit evidence。</summary>
internal static class MemoryCampaignProcessRunner
{
    /// <summary>建立不經 shell 的 dotnet run StartInfo 與固定 CLI arguments。</summary>
    public static ProcessStartInfo CreateStartInfo(
        string repositoryRoot,
        MemoryProbeInvocation invocation,
        string outputRoot,
        string? modelRoot,
        string? policyPath,
        string? campaignId = null,
        MemoryCampaignIdentity? sourceIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetFullPath(repositoryRoot),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine("tools", "Laya.MemoryProbe", "Laya.MemoryProbe.csproj"));
        startInfo.ArgumentList.Add("--");
        foreach (var argument in invocation.ToArguments(outputRoot, modelRoot, policyPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (campaignId is not null)
        {
            startInfo.Environment["LAYA_CAMPAIGN_ID"] = campaignId;
        }

        if (sourceIdentity is not null)
        {
            startInfo.Environment["LAYA_SOURCE_COMMIT"] = sourceIdentity.SourceCommit;
            startInfo.Environment["LAYA_SOURCE_DIRTY"] = sourceIdentity.SourceDirty.ToString().ToLowerInvariant();
            if (sourceIdentity.DirtyIdentity is not null)
            {
                startInfo.Environment["LAYA_SOURCE_DIRTY_IDENTITY"] = sourceIdentity.DirtyIdentity;
            }
        }

        return startInfo;
    }

    /// <summary>執行一個 child Probe process，並將兩個輸出串流直接複製至獨立 log 檔。</summary>
    public static async Task<MemoryCampaignChildResult> RunAsync(
        string repositoryRoot,
        MemoryProbeInvocation invocation,
        string outputRoot,
        string hostLogRoot,
        string? modelRoot,
        string? policyPath,
        CancellationToken cancellationToken = default,
        string? campaignId = null,
        MemoryCampaignIdentity? sourceIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostLogRoot);
        Directory.CreateDirectory(hostLogRoot);
        var outputPath = Path.Combine(hostLogRoot, $"{invocation.RunId}.stdout.log");
        var errorPath = Path.Combine(hostLogRoot, $"{invocation.RunId}.stderr.log");
        using var outputStream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var errorStream = new FileStream(errorPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var process = new Process
        {
            StartInfo = CreateStartInfo(repositoryRoot, invocation, outputRoot, modelRoot, policyPath, campaignId, sourceIdentity)
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the MemoryProbe child process.");
        }

        var copyOutput = process.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken);
        var copyError = process.StandardError.BaseStream.CopyToAsync(errorStream, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(copyOutput, copyError).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(copyOutput, copyError).ConfigureAwait(false);
        outputStream.Flush(flushToDisk: true);
        errorStream.Flush(flushToDisk: true);
        return new MemoryCampaignChildResult(invocation.RunId, process.ExitCode, outputPath, errorPath);
    }
}
