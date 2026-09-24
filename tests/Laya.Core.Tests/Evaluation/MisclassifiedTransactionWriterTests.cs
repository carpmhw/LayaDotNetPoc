using Laya.ConsoleApp.Evaluation;
using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class MisclassifiedTransactionWriterTests
{
    /// <summary>驗證錯誤 CSV 固定八欄、quote escaping 且不輸出原始 description。</summary>
    [Fact]
    public void Write_EmitsRedactedEightColumnCsv()
    {
        var row = new TransactionRow(
            "id-1",
            "Merchant, \"quoted\" account 1234",
            -10,
            "TWD",
            "debit",
            "food",
            "en",
            "synthetic",
            "group=test");
        var prediction = new LayaPhase2Prediction(
            "id-1",
            "food",
            "transfer",
            "en",
            LayaPhase2Labels.Categories.ToDictionary(
                label => label,
                label => label == "transfer" ? 0.8 : 0.2 / 10,
                StringComparer.Ordinal),
            ["transfer", "food", "other"],
            0.8,
            0.6,
            0.3,
            null,
            null);
        var run = new LayaPhase2RunResult(
            1,
            new LayaPhase2RunManifest(
                "run-1", "english", "revision", "dataset", "development", "A", "prompt", null,
                DateTimeOffset.UnixEpoch, "test"),
            new[] { prediction },
            Array.Empty<LayaPhase2Failure>());
        var path = Path.Combine(Path.GetTempPath(), $"misclassified-{Guid.NewGuid():N}.csv");

        try
        {
            new MisclassifiedTransactionWriter().Write(path, run, new[] { row });

            var output = File.ReadAllText(path);
            Assert.StartsWith("id,description_redacted,expected,predicted,confidence,second_choice,margin,language\n", output, StringComparison.Ordinal);
            Assert.DoesNotContain(row.Description, output, StringComparison.Ordinal);
            Assert.Contains("id-1", output, StringComparison.Ordinal);
            Assert.Equal("\"a,\"\"b\"\"\nc\"", MisclassifiedTransactionWriter.EscapeCsvField("a,\"b\"\nc"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
