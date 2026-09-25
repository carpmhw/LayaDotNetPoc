using System.Globalization;

namespace Laya.MemoryBenchmarks.Analysis;

/// <summary>由 per-run raw samples 重算 reproducibility 所需 memory metrics。</summary>
internal static class MemoryCampaignMeasurementReader
{
    /// <summary>讀取 raw CSV 並計算 process PrivateMemory／RSS peak、end 與 natural late slopes。</summary>
    public static MemoryReplicateMeasurement ReadRunMetrics(string runId, string samplesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(samplesPath);
        var rows = ReadRows(samplesPath).ToArray();
        if (rows.Length == 0)
        {
            throw new InvalidDataException($"Run '{runId}' has no raw memory samples.");
        }

        var privateValues = rows.Select(row => ReadRequiredLong(row, "private_memory_bytes", runId)).ToArray();
        var rssValues = rows.Select(row => ReadRequiredLong(row, "vmrss_bytes", runId)).ToArray();
        var naturalRows = rows
            .Where(row => string.Equals(ReadRequiredString(row, "phase", runId), "measurement", StringComparison.Ordinal))
            .ToArray();
        var privateSlope = AnalyzeLateSlope(naturalRows, "private_memory_bytes", runId);
        var rssSlope = AnalyzeLateSlope(naturalRows, "vmrss_bytes", runId);

        return new MemoryReplicateMeasurement(
            runId,
            privateValues.Max(),
            privateValues[^1],
            privateSlope,
            rssValues.Max(),
            rssValues[^1],
            rssSlope);
    }

    /// <summary>重算指定 raw metric 的 2500-5000 natural samples OLS slope。</summary>
    private static double AnalyzeLateSlope(
        IReadOnlyList<Dictionary<string, string>> rows,
        string metric,
        string runId)
    {
        var points = rows.Select(row => new MemorySamplePoint(
                ReadRequiredInt(row, "request_count", runId),
                ReadRequiredLong(row, metric, runId),
                ReadRequiredInt(row, "sample_index", runId),
                isForcedGc: false))
            .ToArray();
        var result = MemorySlopeAnalyzer.AnalyzeSegment(points, 2500, 5000);
        if (!result.IsComplete || result.SlopeBytesPer100Requests is null)
        {
            throw new InvalidDataException($"Run '{runId}' lacks complete natural 2500-5000 samples for '{metric}'.");
        }

        return result.SlopeBytesPer100Requests.Value;
    }

    /// <summary>逐列讀取 RFC 4180 CSV rows 並以 header 建立 lookup。</summary>
    private static IEnumerable<Dictionary<string, string>> ReadRows(string path)
    {
        using var reader = File.OpenText(path);
        using var records = CsvRecordReader.ReadRecords(reader).GetEnumerator();
        if (!records.MoveNext())
        {
            yield break;
        }

        var header = records.Current;
        while (records.MoveNext())
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < header.Count && index < records.Current.Count; index++)
            {
                row[header[index]] = records.Current[index];
            }

            yield return row;
        }
    }

    /// <summary>取得 raw CSV 必要 Int64 欄位，missing／null 時阻擋 replicate comparison。</summary>
    private static long ReadRequiredLong(IReadOnlyDictionary<string, string> row, string name, string runId)
    {
        if (!row.TryGetValue(name, out var text) ||
            !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"Run '{runId}' has unavailable metric '{name}'.");
        }

        return value;
    }

    /// <summary>取得 raw CSV 必要 int 欄位，missing／invalid 時回報 run identity。</summary>
    private static int ReadRequiredInt(IReadOnlyDictionary<string, string> row, string name, string runId)
    {
        if (!row.TryGetValue(name, out var text) ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"Run '{runId}' has an invalid count metric '{name}'.");
        }

        return value;
    }

    /// <summary>取得 raw CSV 必要 non-empty string 欄位。</summary>
    private static string ReadRequiredString(IReadOnlyDictionary<string, string> row, string name, string runId)
    {
        return row.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Run '{runId}' is missing field '{name}'.");
    }
}
