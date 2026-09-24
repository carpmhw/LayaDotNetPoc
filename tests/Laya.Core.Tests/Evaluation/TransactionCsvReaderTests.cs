using Laya.ConsoleApp.Evaluation;
using Laya.Core.Exceptions;

namespace Laya.Core.Tests.Evaluation;

public sealed class TransactionCsvReaderTests
{
    /// <summary>驗證 Phase 2 reader 依 header 映射並保留 quoted comma、quote 與換行。</summary>
    [Fact]
    public void ReadPhase2_ParsesQuotedCommaQuoteAndMultilineFields()
    {
        var extraRows = string.Concat(Enumerable.Range(2, 199).Select(index =>
            $"en,,tx-{index},Cafe {index},-1,TWD,debit,food,synthetic\n"));
        var path = WriteTempFile(
            "language,notes,id,description,amount,currency,direction,expected_category,source_type\n" +
            "zh,\"line one\nline two\",tx-1,\"Cafe, \"\"Taipei\"\"\",-120.5,TWD,debit,food,synthetic\n" +
            extraRows);

        try
        {
            var rows = new TransactionCsvReader().Read(path, TransactionCsvPhase.Phase2);

            Assert.Equal(200, rows.Count);
            var row = rows[0];
            Assert.Equal("tx-1", row.Id);
            Assert.Equal("Cafe, \"Taipei\"", row.Description);
            Assert.Equal("line one\nline two", row.Notes);
            Assert.Equal(-120.5, row.Amount, precision: 12);
            Assert.Equal("food", row.ExpectedCategory);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>驗證重複 ID、enum、label 與 amount 會在推論前拒絕。</summary>
    [Fact]
    public void ReadPhase2_RejectsDuplicateIdAndInvalidValues()
    {
        var path = WriteTempFile(
            "id,description,amount,currency,direction,language,expected_category,source_type,notes\n" +
            "tx-1,one,not-a-number,TWD,wrong,en,food,synthetic,\n" +
            "tx-1,two,2,TWD,debit,en,not-a-category,synthetic,\n");

        try
        {
            var exception = Assert.Throws<LayaConfigurationException>(() =>
                new TransactionCsvReader().Read(path, TransactionCsvPhase.Phase2));

            Assert.Contains("amount", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>驗證 Phase 1 保留七欄且明確要求 30 至 50 筆。</summary>
    [Fact]
    public void ReadPhase1_PreservesLegacyRowCountContract()
    {
        var header = "id,description,amount,currency,direction,expected_category,language\n";
        var rows = string.Concat(Enumerable.Range(1, 30).Select(index =>
            $"tx-{index},Coffee {index},-1,TWD,debit,food,en\n"));
        var path = WriteTempFile(header + rows);

        try
        {
            var parsed = new TransactionCsvReader().Read(path, TransactionCsvPhase.Phase1);

            Assert.Equal(30, parsed.Count);
            Assert.Null(parsed[0].SourceType);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>驗證 Phase 2 不接受低於 200 筆的資料集。</summary>
    [Fact]
    public void ReadPhase2_RejectsDatasetOutsideAllowedSize()
    {
        var header = "id,description,amount,currency,direction,language,expected_category,source_type,notes\n";
        var rows = string.Concat(Enumerable.Range(1, 199).Select(index =>
            $"tx-{index},Coffee {index},-1,TWD,debit,en,food,synthetic,\n"));
        var path = WriteTempFile(header + rows);

        try
        {
            var exception = Assert.Throws<LayaConfigurationException>(() =>
                new TransactionCsvReader().Read(path, TransactionCsvPhase.Phase2));

            Assert.Contains("200-500", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>驗證提交的 Phase 2 synthetic dataset 符合固定 labels、語言與 group 數量。</summary>
    [Fact]
    public void ReadPhase2_RepositoryDatasetHasReproducibleDistribution()
    {
        var path = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../test-data/transactions-phase2.csv"));

        var rows = new TransactionCsvReader().Read(path, TransactionCsvPhase.Phase2);

        Assert.Equal(220, rows.Count);
        Assert.Equal(20, rows.GroupBy(row => row.ExpectedCategory).Select(group => group.Count()).Min());
        Assert.Equal(new[] { "en", "mixed", "zh" }, rows.Select(row => row.Language).Distinct().OrderBy(value => value));
        Assert.All(rows, row => Assert.Matches("^group=", row.Notes ?? string.Empty));
        Assert.All(
            rows.GroupBy(row => row.Notes),
            group => Assert.InRange(group.Count(), 1, 5));
    }

    /// <summary>建立供 CSV reader 測試使用的暫存檔。</summary>
    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"laya-transactions-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }
}
