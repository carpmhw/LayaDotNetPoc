using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ML.OnnxRuntime;

namespace Laya.MemoryProbe.Reporting;

/// <summary>擷取正式 run 所需 host environment 與 source-tree identity。</summary>
internal static class MemoryProbeHostMetadata
{
    /// <summary>擷取 OS／kernel／CPU／RAM／swap／SDK／runtime／ORT 環境欄位。</summary>
    public static Dictionary<string, object?> CaptureEnvironment()
    {
        var memoryLimit = ReadCgroupLimit("memory.max");
        var swapLimit = ReadCgroupLimit("memory.swap.max");
        var sdkVersion = Environment.GetEnvironmentVariable("DOTNET_SDK_VERSION") ?? ReadDotnetSdkVersion() ?? "not-installed";
        var environment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["kernel"] = ReadKernelVersion(),
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["cpu"] = ReadCpuModel(),
            ["logicalCores"] = Environment.ProcessorCount,
            ["ramBytes"] = ReadProcMemoryBytes("MemTotal") ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            ["swapBytes"] = ReadProcMemoryBytes("SwapTotal") ?? 0,
            ["dotnetSdk"] = sdkVersion,
            ["dotnetRuntime"] = RuntimeInformation.FrameworkDescription,
            ["onnxRuntime"] = typeof(InferenceSession).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["executionProvider"] = "CPU",
            ["containerRuntime"] = Environment.GetEnvironmentVariable("CONTAINER_RUNTIME"),
            ["memoryLimitBytes"] = memoryLimit,
            ["memorySwapLimitBytes"] = swapLimit
        };
        return environment;
    }

    /// <summary>擷取 commit、dirty 狀態及 worktree 內容摘要，不保存原始 diff。</summary>
    public static Dictionary<string, object?> CaptureSourceIdentity(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var suppliedCommit = Environment.GetEnvironmentVariable("LAYA_SOURCE_COMMIT");
        if (!string.IsNullOrWhiteSpace(suppliedCommit))
        {
            var suppliedDirty = bool.TryParse(Environment.GetEnvironmentVariable("LAYA_SOURCE_DIRTY"), out var dirtyValue)
                ? dirtyValue
                : true;
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["commit"] = suppliedCommit,
                ["dirty"] = suppliedDirty,
                ["dirtyIdentity"] = suppliedDirty
                    ? Environment.GetEnvironmentVariable("LAYA_SOURCE_DIRTY_IDENTITY") ?? "source-identity-unavailable"
                    : null
            };
        }

        var root = Path.GetFullPath(repositoryRoot);
        var commit = RunGit(root, "rev-parse", "HEAD");
        var status = FilterEvidenceOutput(RunGit(root, "status", "--porcelain=v1", "--untracked-files=all"));
        if (commit is null || status is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["commit"] = Environment.GetEnvironmentVariable("LAYA_SOURCE_COMMIT") ?? "unknown",
                ["dirty"] = true,
                ["dirtyIdentity"] = Environment.GetEnvironmentVariable("LAYA_SOURCE_DIRTY_IDENTITY") ?? "source-identity-unavailable"
            };
        }

        var isDirty = status.Length > 0;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["commit"] = commit,
            ["dirty"] = isDirty,
            ["dirtyIdentity"] = isDirty ? ComputeDirtyIdentity(root, status) : null
        };
    }

    /// <summary>排除 Phase 3A evidence output，以免 run artifacts 改變自身 source identity。</summary>
    private static string? FilterEvidenceOutput(string? status)
    {
        if (status is null)
        {
            return null;
        }

        return string.Join(
            '\n',
            status.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Length < 4 || !IsExcludedIdentityPath(line[3..])));
    }

    /// <summary>判斷本機產生的 evidence 與預存 repository guidance 是否排除於 source identity。</summary>
    private static bool IsExcludedIdentityPath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        return string.Equals(normalizedPath, "AGENTS.md", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("reports/phase3a/", StringComparison.Ordinal);
    }

    /// <summary>執行 `dotnet --version`，不在 runtime-only image 下載或安裝 SDK。</summary>
    private static string? ReadDotnetSdkVersion()
    {
        return RunProcess("dotnet", "--version", Environment.CurrentDirectory);
    }

    /// <summary>從 Linux proc 讀取 kernel release，其他平台回報 OS kernel 版本字串。</summary>
    private static string ReadKernelVersion()
    {
        try
        {
            return OperatingSystem.IsLinux()
                ? File.ReadAllText("/proc/sys/kernel/osrelease").Trim()
                : Environment.OSVersion.VersionString;
        }
        catch (IOException)
        {
            return Environment.OSVersion.VersionString;
        }
    }

    /// <summary>讀取 Linux CPU model name，讀取失敗時使用 runtime 架構識別。</summary>
    private static string ReadCpuModel()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    var separatorIndex = line.IndexOf(':');
                    if (separatorIndex > 0 && line[..separatorIndex].Trim() is "model name" or "Hardware")
                    {
                        return line[(separatorIndex + 1)..].Trim();
                    }
                }
            }
            catch (IOException)
            {
            }
        }

        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    /// <summary>讀取 `/proc/meminfo` 指定欄位並將 kB 換算成 bytes。</summary>
    private static long? ReadProcMemoryBytes(string name)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0 || !string.Equals(line[..separatorIndex], name, StringComparison.Ordinal))
                {
                    continue;
                }

                var value = line[(separatorIndex + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                return value.Length == 2 &&
                    value[1] == "kB" &&
                    long.TryParse(value[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kiloBytes)
                        ? checked(kiloBytes * 1024)
                        : null;
            }
        }
        catch (IOException)
        {
        }

        return null;
    }

    /// <summary>從 current cgroup 或 mounted cgroup root 讀取 memory limit bytes。</summary>
    private static long? ReadCgroupLimit(string fileName)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var candidates = new List<string>();
        try
        {
            foreach (var line in File.ReadLines("/proc/self/cgroup"))
            {
                var parts = line.Split(':', 3);
                if (parts.Length == 3 && parts[0] == "0")
                {
                    var relativePath = parts[2].TrimStart(Path.DirectorySeparatorChar);
                    candidates.Add(Path.Combine("/sys/fs/cgroup", relativePath, fileName));
                }
            }
        }
        catch (IOException)
        {
        }

        candidates.Add(Path.Combine("/sys/fs/cgroup", fileName));
        foreach (var path in candidates.Distinct(StringComparer.Ordinal))
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var value = File.ReadAllText(path).Trim();
                if (value == "max")
                {
                    return null;
                }

                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes >= 0)
                {
                    return bytes;
                }
            }
            catch (IOException)
            {
            }
        }

        return null;
    }

    /// <summary>計算 git diff、status 與 untracked source 檔案內容的 SHA-256。</summary>
    private static string ComputeDirtyIdentity(string repositoryRoot, string status)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendUtf8(hash, status);
        AppendUtf8(hash, RunGit(repositoryRoot, "diff", "--binary", "HEAD", "--", ".", ":(exclude)reports/phase3a", ":(exclude)AGENTS.md") ?? string.Empty);
        var untracked = RunGit(repositoryRoot, "ls-files", "--others", "--exclude-standard", "-z") ?? string.Empty;
        foreach (var relativePath in untracked.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsExcludedIdentityPath(relativePath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            AppendUtf8(hash, relativePath);
            using var stream = File.OpenRead(fullPath);
            var buffer = new byte[64 * 1024];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>以 UTF-8 將一段 dirty identity metadata append 至 hash。</summary>
    private static void AppendUtf8(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>執行 git／dotnet process 並取得成功時的 trimmed stdout。</summary>
    private static string? RunGit(string workingDirectory, params string[] arguments)
    {
        return RunProcess("git", arguments, workingDirectory);
    }

    /// <summary>以參數陣列啟動子程序；無法啟動或非零結束時回傳 null。</summary>
    private static string? RunProcess(string executable, string argument, string workingDirectory)
    {
        return RunProcess(executable, new[] { argument }, workingDirectory);
    }

    /// <summary>以參數陣列啟動子程序，避免透過 shell 解譯 command text。</summary>
    private static string? RunProcess(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
