using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Laya.Core.Evaluation;

namespace Laya.ConsoleApp.Evaluation;

/// <summary>輸出固定八欄且不含原始交易描述的誤分類 CSV。</summary>
public sealed class MisclassifiedTransactionWriter
{
    private const string Header =
        "id,description_redacted,expected,predicted,confidence,second_choice,margin,language";

    /// <summary>寫入指定 run 的錯誤列；沒有錯誤時仍保留表頭。</summary>
    public void Write(
        string path,
        LayaPhase2RunResult run,
        IReadOnlyList<TransactionRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rows);
        var rowById = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var builder = new StringBuilder().Append(Header).Append('\n');
        foreach (var prediction in run.Predictions.Where(item => item.ExpectedLabel != item.PredictedLabel))
        {
            if (!rowById.TryGetValue(prediction.Id, out var row))
            {
                throw new InvalidOperationException(
                    $"Run '{run.Manifest.RunId}' has no source row for prediction '{prediction.Id}'.");
            }

            var secondChoice = prediction.RankedLabels.Count > 1
                ? prediction.RankedLabels[1]
                : string.Empty;
            var fields = new[]
            {
                row.Id,
                RedactDescription(row.Description),
                prediction.ExpectedLabel,
                prediction.PredictedLabel,
                prediction.Probability.ToString("R", CultureInfo.InvariantCulture),
                secondChoice,
                prediction.Margin.ToString("R", CultureInfo.InvariantCulture),
                row.Language
            };
            builder.Append(string.Join(',', fields.Select(EscapeCsvField))).Append('\n');
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>依固定 hash 建立不可逆 description surrogate，避免洩漏原文。</summary>
    private static string RedactDescription(string description)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(description))).ToLowerInvariant();
        return $"redacted-{hash[..16]}";
    }

    /// <summary>依 RFC 4180 escape comma、雙引號與換行欄位。</summary>
    public static string EscapeCsvField(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
