using System.Globalization;
using System.Text;
using Laya.Core.Exceptions;

namespace Laya.ConsoleApp.Evaluation;

/// <summary>指定交易 CSV 的資料契約版本。</summary>
public enum TransactionCsvPhase
{
    /// <summary>Phase 1 的七欄、30 至 50 筆相容入口。</summary>
    Phase1,

    /// <summary>Phase 2 的九欄、200 至 500 筆評估入口。</summary>
    Phase2
}

/// <summary>保存 CSV 解析後且尚未送入模型的交易欄位。</summary>
public sealed record TransactionRow(
    string Id,
    string Description,
    double Amount,
    string Currency,
    string Direction,
    string ExpectedCategory,
    string Language,
    string? SourceType,
    string? Notes);

/// <summary>以 RFC 4180 狀態機讀取並驗證兩個 Phase 的交易 CSV。</summary>
public sealed class TransactionCsvReader
{
    /// <summary>Phase 2 固定使用的 11 類選項順序。</summary>
    public static IReadOnlyList<string> CategoryLabels { get; } = new[]
    {
        "food", "transport", "shopping", "utilities", "transfer", "salary",
        "bank_fee", "investment", "medical", "entertainment", "other"
    };

    /// <summary>讀取指定 phase 的 CSV，任何列級錯誤都會阻止回傳部分資料。</summary>
    public IReadOnlyList<TransactionRow> Read(string path, TransactionCsvPhase phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new LayaConfigurationException($"Transaction CSV was not found: {path}");
        }

        var records = ParseRecords(path);
        if (records.Count == 0)
        {
            throw new LayaConfigurationException("Transaction CSV is empty.");
        }

        var requiredHeaders = phase == TransactionCsvPhase.Phase1
            ? new[] { "id", "description", "amount", "currency", "direction", "expected_category", "language" }
            : new[] { "id", "description", "amount", "currency", "direction", "language", "expected_category", "source_type", "notes" };
        var headerIndexes = CreateHeaderIndexes(records[0], requiredHeaders);
        var rows = new List<TransactionRow>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 1; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (record.Fields.Count != requiredHeaders.Length)
            {
                throw new LayaConfigurationException(
                    $"Transaction CSV row {record.StartLine} must contain {requiredHeaders.Length} columns.");
            }

            var row = CreateRow(record, headerIndexes, phase);
            if (!seenIds.Add(row.Id))
            {
                throw new LayaConfigurationException(
                    $"Transaction CSV row {record.StartLine} repeats transaction id '{row.Id}'.");
            }

            rows.Add(row);
        }

        ValidateRowCount(rows.Count, phase);
        return rows;
    }

    /// <summary>建立 header 到欄位索引的映射並拒絕缺欄或重複欄名。</summary>
    private static IReadOnlyDictionary<string, int> CreateHeaderIndexes(
        CsvRecord header,
        IReadOnlyList<string> requiredHeaders)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < header.Fields.Count; index++)
        {
            if (!indexes.TryAdd(header.Fields[index], index))
            {
                throw new LayaConfigurationException(
                    $"Transaction CSV header repeats column '{header.Fields[index]}'.");
            }
        }

        var missing = requiredHeaders.Where(name => !indexes.ContainsKey(name)).ToArray();
        if (missing.Length > 0 || header.Fields.Count != requiredHeaders.Count)
        {
            throw new LayaConfigurationException(
                $"Transaction CSV header is invalid; missing: {string.Join(", ", missing)}.");
        }

        return indexes;
    }

    /// <summary>依欄名映射建立交易 row 並驗證 phase 專用 enum 與數值。</summary>
    private static TransactionRow CreateRow(
        CsvRecord record,
        IReadOnlyDictionary<string, int> indexes,
        TransactionCsvPhase phase)
    {
        var id = GetValue(record, indexes, "id");
        var description = GetValue(record, indexes, "description");
        var amountText = GetValue(record, indexes, "amount");
        var currency = GetValue(record, indexes, "currency");
        var direction = GetValue(record, indexes, "direction");
        var language = GetValue(record, indexes, "language");
        var expectedCategory = GetValue(record, indexes, "expected_category");
        var sourceType = phase == TransactionCsvPhase.Phase2
            ? GetValue(record, indexes, "source_type")
            : null;
        var notes = phase == TransactionCsvPhase.Phase2
            ? GetValue(record, indexes, "notes")
            : null;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(description) ||
            string.IsNullOrWhiteSpace(currency))
        {
            throw new LayaConfigurationException(
                $"Transaction CSV row {record.StartLine} has a missing required value.");
        }

        if (!double.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ||
            !double.IsFinite(amount))
        {
            throw new LayaConfigurationException(
                $"Transaction CSV row {record.StartLine} has an invalid amount.");
        }

        if (direction is not ("debit" or "credit"))
        {
            throw new LayaConfigurationException(
                $"Transaction CSV row {record.StartLine} has invalid direction '{direction}'.");
        }

        var validLanguages = phase == TransactionCsvPhase.Phase1
            ? new[] { "en", "zh" }
            : new[] { "en", "zh", "mixed" };
        if (!validLanguages.Contains(language, StringComparer.Ordinal))
        {
            throw new LayaConfigurationException(
                $"Transaction CSV row {record.StartLine} has invalid language '{language}'.");
        }

        if (!CategoryLabels.Contains(expectedCategory, StringComparer.Ordinal))
        {
            throw new LayaConfigurationException(
                $"Transaction CSV row {record.StartLine} has invalid category '{expectedCategory}'.");
        }

        if (phase == TransactionCsvPhase.Phase2 && sourceType is not ("synthetic" or "de-identified"))
        {
            throw new LayaConfigurationException(
                $"Transaction CSV row {record.StartLine} has invalid source_type '{sourceType}'.");
        }

        return new TransactionRow(
            id,
            description,
            amount,
            currency,
            direction,
            expectedCategory,
            language,
            sourceType,
            notes);
    }

    /// <summary>取得欄位值並將缺失索引轉成明確 CSV 診斷。</summary>
    private static string GetValue(
        CsvRecord record,
        IReadOnlyDictionary<string, int> indexes,
        string name)
    {
        if (!indexes.TryGetValue(name, out var index) || index >= record.Fields.Count)
        {
            throw new LayaConfigurationException(
                $"Transaction CSV row {record.StartLine} is missing column '{name}'.");
        }

        return record.Fields[index];
    }

    /// <summary>驗證 phase 對資料筆數的固定邊界。</summary>
    private static void ValidateRowCount(int count, TransactionCsvPhase phase)
    {
        var valid = phase == TransactionCsvPhase.Phase1
            ? count is >= 30 and <= 50
            : count is >= 200 and <= 500;
        if (!valid)
        {
            var range = phase == TransactionCsvPhase.Phase1 ? "30-50" : "200-500";
            throw new LayaConfigurationException(
                $"Transaction CSV for {phase} must contain {range} data rows; actual count was {count}.");
        }
    }

    /// <summary>以 RFC 4180 規則解析 quoted comma、escaped quote 與 multiline record。</summary>
    private static IReadOnlyList<CsvRecord> ParseRecords(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var afterClosingQuote = false;
        var line = 1;
        var recordStartLine = 1;

        while (reader.Read() is var code && code >= 0)
        {
            var character = (char)code;
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        _ = reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        afterClosingQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                    if (character == '\n')
                    {
                        line++;
                    }
                }

                continue;
            }

            if (afterClosingQuote)
            {
                if (character == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    afterClosingQuote = false;
                }
                else if (character == '\n')
                {
                    AddRecord(records, fields, field, recordStartLine);
                    line++;
                    recordStartLine = line;
                    afterClosingQuote = false;
                }
                else if (character != '\r')
                {
                    throw new LayaConfigurationException(
                        $"Transaction CSV row {recordStartLine} has characters after a closing quote.");
                }

                continue;
            }

            if (character == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (character == '"')
            {
                throw new LayaConfigurationException(
                    $"Transaction CSV row {recordStartLine} contains an unescaped quote.");
            }
            else if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\n')
            {
                AddRecord(records, fields, field, recordStartLine);
                line++;
                recordStartLine = line;
            }
            else if (character != '\r')
            {
                field.Append(character);
            }
        }

        if (inQuotes)
        {
            throw new LayaConfigurationException(
                $"Transaction CSV row {recordStartLine} contains an unclosed quote.");
        }

        if (field.Length > 0 || fields.Count > 0 || afterClosingQuote)
        {
            AddRecord(records, fields, field, recordStartLine);
        }

        return records;
    }

    /// <summary>完成一筆解析中的 record 並清空 parser 狀態。</summary>
    private static void AddRecord(
        ICollection<CsvRecord> records,
        ICollection<string> fields,
        StringBuilder field,
        int startLine)
    {
        fields.Add(field.ToString());
        records.Add(new CsvRecord(fields.ToArray(), startLine));
        fields.Clear();
        field.Clear();
    }

    /// <summary>保存 CSV record 的欄位與起始行號。</summary>
    private sealed record CsvRecord(IReadOnlyList<string> Fields, int StartLine);
}
