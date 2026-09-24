using System.Globalization;
using Laya.Core.Inference;
using Laya.Core.Models;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "EnglishModel")]
public sealed class LayaTransactionIntegrationTests
{
    /// <summary>驗證 CSV 每筆交易回傳合法答案、完整機率分布與正的 Run latency。</summary>
    [Fact]
    public void CsvTransactions_ReturnLegalAnswersAndLatency()
    {
        var labels = new[]
        {
            "food", "transport", "shopping", "utilities", "transfer", "salary", "bank_fee", "investment", "medical", "entertainment", "other"
        };
        var rows = ReadRows();
        using var engine = LayaDecisionEngine.Open(FindModelRoot());

        foreach (var row in rows)
        {
            var result = engine.Decide(CreateRequest(row, labels));
            var category = Assert.Single(result.Answers, answer => answer.Name == "category");
            var review = Assert.Single(result.Answers, answer => answer.Name == "needs_review");

            Assert.Contains(category.SelectedOption, labels);
            Assert.Equal(labels.Length, category.Probabilities.Count);
            Assert.Equal(1d, category.Probabilities.Values.Sum(), precision: 6);
            Assert.InRange(category.Probability, 0d, 1d);
            Assert.Equal(2, review.Probabilities.Count);
            Assert.Equal(1d, review.Probabilities.Values.Sum(), precision: 6);
            Assert.InRange(review.Probability, 0d, 1d);
            Assert.True(result.InferenceDuration > TimeSpan.Zero);
        }

        Assert.InRange(rows.Count, 30, 50);
    }

    /// <summary>讀取固定 CSV 欄位並解析 transaction 的必要值。</summary>
    private static IReadOnlyList<TransactionRow> ReadRows()
    {
        var path = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../test-data/transactions.csv"));
        var lines = File.ReadAllLines(path);
        return lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .Select(columns => new TransactionRow(
                columns[1],
                double.Parse(columns[2], CultureInfo.InvariantCulture),
                columns[3],
                columns[4]))
            .ToArray();
    }

    /// <summary>建立與 Console 相同的 11 類 Choice 與 needs_review Noul request。</summary>
    private static LayaRequest CreateRequest(TransactionRow row, IReadOnlyList<string> labels)
    {
        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["description"] = row.Description,
            ["amount"] = row.Amount,
            ["currency"] = row.Currency,
            ["direction"] = row.Direction
        };

        return new LayaRequest(
            state,
            new[]
            {
                new LayaQuestion("category", LayaQuestionType.Choice, "Choose the best transaction category", labels),
                new LayaQuestion("needs_review", LayaQuestionType.Noul, "Is this transaction uncertain?")
            });
    }

    /// <summary>尋找 repository 內的本機模型目錄，允許 CI 透過環境變數覆寫。</summary>
    private static string FindModelRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LAYA_MODEL_ROOT");
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../models/laya"));
    }

    /// <summary>保存整合測試使用的 transaction 欄位。</summary>
    private sealed record TransactionRow(string Description, double Amount, string Currency, string Direction);
}
