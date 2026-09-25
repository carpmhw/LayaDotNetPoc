extern alias MemoryProbe;

using MemoryProbe::Laya.MemoryProbe.Reporting;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryProbeHostMetadataTests
{
    /// <summary>驗證環境 snapshot 記錄 .NET、CPU、RAM、swap 與 execution provider。</summary>
    [Fact]
    public void CaptureEnvironment_RecordsRequiredHostIdentity()
    {
        var environment = MemoryProbeHostMetadata.CaptureEnvironment();

        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(environment["os"])));
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(environment["kernel"])));
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(environment["cpu"])));
        Assert.True(Assert.IsType<int>(environment["logicalCores"]) > 0);
        Assert.True(Assert.IsType<long>(environment["ramBytes"]) > 0);
        Assert.True(Assert.IsType<long>(environment["swapBytes"]) >= 0);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(environment["dotnetSdk"])));
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(environment["dotnetRuntime"])));
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(environment["onnxRuntime"])));
        Assert.Equal("CPU", environment["executionProvider"]);
    }

    /// <summary>驗證 source identity 保存 commit 及 dirty worktree 指紋或明確 unavailable 狀態。</summary>
    [Fact]
    public void CaptureSourceIdentity_RecordsCommitAndDirtyState()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

        var identity = MemoryProbeHostMetadata.CaptureSourceIdentity(repositoryRoot);

        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(identity["commit"])));
        Assert.IsType<bool>(identity["dirty"]);
        if (Assert.IsType<bool>(identity["dirty"]))
        {
            Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(identity["dirtyIdentity"])));
        }
        else
        {
            Assert.Null(identity["dirtyIdentity"]);
        }
    }

    /// <summary>驗證既有 AGENTS.md 不會被納入 Probe source dirty identity。</summary>
    [Fact]
    public void CaptureSourceIdentity_ExcludesRepositoryGuidanceFile()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"phase3a-identity-agents-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryRoot);
        try
        {
            RunGit(repositoryRoot, "init", "--quiet");
            RunGit(repositoryRoot, "-c", "user.name=Phase3A Test", "-c", "user.email=phase3a@example.invalid", "commit", "--allow-empty", "-m", "initial");
            File.WriteAllText(Path.Combine(repositoryRoot, "AGENTS.md"), "first local guidance contents");

            var firstIdentity = MemoryProbeHostMetadata.CaptureSourceIdentity(repositoryRoot);
            File.WriteAllText(Path.Combine(repositoryRoot, "AGENTS.md"), "changed local guidance contents");
            var secondIdentity = MemoryProbeHostMetadata.CaptureSourceIdentity(repositoryRoot);

            Assert.Equal(firstIdentity["commit"], secondIdentity["commit"]);
            Assert.Equal(false, firstIdentity["dirty"]);
            Assert.Equal(firstIdentity["dirtyIdentity"], secondIdentity["dirtyIdentity"]);
            Assert.Null(firstIdentity["dirtyIdentity"]);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    /// <summary>在隔離 repository 執行 Git 指令並於失敗時保留錯誤輸出。</summary>
    private static void RunGit(string repositoryRoot, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Git for the isolated identity test.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"Git failed: {standardError}{standardOutput}");
    }
}
