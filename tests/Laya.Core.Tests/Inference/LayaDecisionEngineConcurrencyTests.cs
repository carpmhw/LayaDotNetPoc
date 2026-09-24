using System.Collections.Concurrent;
using Laya.Core.Inference;
using Laya.Core.Models;

namespace Laya.Core.Tests.Inference;

[Trait("Category", "EnglishModel")]
public sealed class LayaDecisionEngineConcurrencyTests
{
    /// <summary>驗證共享長生命週期 engine 在 concurrency 4 下維持 request 結果完整性。</summary>
    [Fact]
    public async Task Decide_PreservesAnswersAcrossConcurrentRequests()
    {
        using var engine = LayaDecisionEngine.Open(FindModelRoot());
        var requests = Enumerable.Range(0, 8)
            .Select(CreateRequest)
            .ToArray();
        var expected = requests
            .Select(engine.Decide)
            .Select(result => result.Answers.Select(answer => answer.SelectedOption).ToArray())
            .ToArray();
        var actual = new ConcurrentDictionary<int, string?[]>();

        await Parallel.ForEachAsync(
                Enumerable.Range(0, requests.Length),
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                (index, _) =>
                {
                    actual[index] = engine.Decide(requests[index])
                        .Answers
                        .Select(answer => answer.SelectedOption)
                        .ToArray();
                    return ValueTask.CompletedTask;
                });

        Assert.Equal(requests.Length, actual.Count);
        for (var index = 0; index < requests.Length; index++)
        {
            Assert.Equal(expected[index], actual[index]);
        }
    }

    /// <summary>建立具有穩定四欄 state、Choice 與 Noul 的 synthetic request。</summary>
    private static LayaRequest CreateRequest(int index)
    {
        return new LayaRequest(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["description"] = "Concurrency test transaction",
                ["amount"] = 420 + index,
                ["currency"] = "TWD",
                ["direction"] = index % 2 == 0 ? "debit" : "credit"
            },
            new[]
            {
                new LayaQuestion(
                    "category",
                    LayaQuestionType.Choice,
                    "Choose a category",
                    new[] { "food", "transport", "shopping" }),
                new LayaQuestion("needs_review", LayaQuestionType.Noul, "Is this uncertain?")
            });
    }

    /// <summary>尋找 repository 內的 English model，允許 CI 透過環境變數覆寫。</summary>
    private static string FindModelRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LAYA_MODEL_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../models/laya"));
    }
}
