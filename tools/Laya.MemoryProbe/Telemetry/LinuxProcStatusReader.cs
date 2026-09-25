using System.Globalization;

namespace Laya.MemoryProbe.Telemetry;

/// <summary>保存 Linux `/proc/self/status` 指定欄位的 bytes snapshot。</summary>
internal sealed class LinuxProcStatusSnapshot
{
    /// <summary>建立 Linux process status memory snapshot。</summary>
    public LinuxProcStatusSnapshot(
        MemoryMetricValue vmRss,
        MemoryMetricValue vmSize,
        MemoryMetricValue vmData,
        MemoryMetricValue rssAnon,
        MemoryMetricValue rssFile,
        MemoryMetricValue rssShmem)
    {
        VmRss = vmRss;
        VmSize = vmSize;
        VmData = vmData;
        RssAnon = rssAnon;
        RssFile = rssFile;
        RssShmem = rssShmem;
    }

    /// <summary>取得 resident set size bytes。</summary>
    public MemoryMetricValue VmRss { get; }

    /// <summary>取得 virtual memory size bytes。</summary>
    public MemoryMetricValue VmSize { get; }

    /// <summary>取得 data segment size bytes。</summary>
    public MemoryMetricValue VmData { get; }

    /// <summary>取得 anonymous RSS bytes。</summary>
    public MemoryMetricValue RssAnon { get; }

    /// <summary>取得 file-backed RSS bytes。</summary>
    public MemoryMetricValue RssFile { get; }

    /// <summary>取得 shared-memory RSS bytes。</summary>
    public MemoryMetricValue RssShmem { get; }

    /// <summary>建立所有 Linux proc metrics 都不可用的 snapshot。</summary>
    public static LinuxProcStatusSnapshot Unavailable(string reason)
    {
        return new LinuxProcStatusSnapshot(
            MemoryMetricValue.Unavailable(reason),
            MemoryMetricValue.Unavailable(reason),
            MemoryMetricValue.Unavailable(reason),
            MemoryMetricValue.Unavailable(reason),
            MemoryMetricValue.Unavailable(reason),
            MemoryMetricValue.Unavailable(reason));
    }
}

/// <summary>讀取並解析 Linux process status memory counters。</summary>
internal static class LinuxProcStatusReader
{
    private const long BytesPerKilobyte = 1024;

    /// <summary>讀取 proc status 檔案；不可讀取時回傳 null metrics 與原因。</summary>
    public static LinuxProcStatusSnapshot Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return LinuxProcStatusSnapshot.Unavailable($"Linux proc status unavailable: {exception.Message}");
        }
    }

    /// <summary>解析 VmRSS／VmSize／VmData／RssAnon／RssFile／RssShmem kB counters。</summary>
    public static LinuxProcStatusSnapshot Parse(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var values = new Dictionary<string, MemoryMetricValue>(StringComparer.Ordinal);
        using var reader = new StringReader(contents);
        while (reader.ReadLine() is { } line)
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = line[..separatorIndex];
            if (!IsRequiredField(name))
            {
                continue;
            }

            values[name] = ParseKilobyteValue(name, line[(separatorIndex + 1)..]);
        }

        return new LinuxProcStatusSnapshot(
            GetMetric(values, "VmRSS"),
            GetMetric(values, "VmSize"),
            GetMetric(values, "VmData"),
            GetMetric(values, "RssAnon"),
            GetMetric(values, "RssFile"),
            GetMetric(values, "RssShmem"));
    }

    /// <summary>確認欄位是否是本階段要求的 Linux memory counter。</summary>
    private static bool IsRequiredField(string name)
    {
        return name is "VmRSS" or "VmSize" or "VmData" or "RssAnon" or "RssFile" or "RssShmem";
    }

    /// <summary>解析 kB counter 並以 checked arithmetic 換算 bytes。</summary>
    private static MemoryMetricValue ParseKilobyteValue(string name, string content)
    {
        var parts = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kiloBytes))
        {
            return MemoryMetricValue.Unavailable($"Linux proc field {name} has an invalid numeric value.");
        }

        if (!string.Equals(parts[1], "kB", StringComparison.Ordinal))
        {
            return MemoryMetricValue.Unavailable($"Linux proc field {name} has an unsupported unit '{parts[1]}'.");
        }

        try
        {
            return MemoryMetricValue.Available(checked(kiloBytes * BytesPerKilobyte));
        }
        catch (OverflowException)
        {
            return MemoryMetricValue.Unavailable($"Linux proc field {name} overflows bytes conversion.");
        }
    }

    /// <summary>取得指定 metric，並為未出現的欄位建立不可用原因。</summary>
    private static MemoryMetricValue GetMetric(
        IReadOnlyDictionary<string, MemoryMetricValue> values,
        string name)
    {
        return values.TryGetValue(name, out var value)
            ? value
            : MemoryMetricValue.Unavailable($"Linux proc field {name} is missing.");
    }
}
