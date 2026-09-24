using System.Text.Json;
using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Models;
using Laya.Core.Tokenization;

namespace Laya.Core.Tests.Encoding;

/// <summary>驗證 multilingual 官方 fixture 的 question sequence 與 marker trace。</summary>
[Trait("Category", "MultilingualTokenizer")]
public sealed class MultilingualSequenceParityTests
{
    /// <summary>驗證 criteria key 作為答案、value 以官方 key:value 形式進入 prompt。</summary>
    [Fact]
    public void Build_CriteriaMappingPreservesAnswerKeysAndOfficialPrompt()
    {
        var modelRoot = FindMultilingualModelRoot();
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        var question = LayaQuestion.FromCriteria(
            "category",
            LayaQuestionType.Choice,
            "Pick one",
            new[]
            {
                new KeyValuePair<string, string>("food", "food"),
                new KeyValuePair<string, string>("travel", "travel")
            });

        var batch = new LayaSequenceBuilder(tokenizer, config).Build(
            new LayaRequest("merchant state", new[] { question }));

        Assert.Equal(new[] { "food", "travel" }, question.Options);
        Assert.Equal(new[] { "food: food", "travel: travel" }, question.PromptOptions);
        Assert.Equal(new[] { 7, 11 }, batch.Questions[0].MarkerPositions);
    }

    /// <summary>核對官方第一筆 Choice fixture 的完整 token 與 marker 離散值。</summary>
    [Fact]
    public void Build_MatchesOfficialChoiceTrace()
    {
        var modelRoot = FindMultilingualModelRoot();
        var fixturePath = FindRepositoryFile("test-data", "multilingual-parity-fixtures.json");
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var fixture = document.RootElement.GetProperty("fixtures")[0];
        var trace = fixture.GetProperty("trace");
        var sequence = trace.GetProperty("sequences")[0];
        var expectedTokenIds = sequence.GetProperty("tokenIds").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        var expectedMarkers = sequence.GetProperty("markerPositions").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();

        var batch = new LayaSequenceBuilder(tokenizer, config).Build(
            new LayaRequest(
                fixture.GetProperty("state").GetString(),
                new[]
                {
                    new LayaQuestion(
                        "category",
                        LayaQuestionType.Choice,
                        "Pick one",
                        new[] { "food: food", "travel: travel" })
                }));

        Assert.Equal(expectedTokenIds, batch.Questions[0].TokenIds);
        Assert.Equal(expectedMarkers, batch.Questions[0].MarkerPositions);
    }

    /// <summary>依環境變數或 repository 相對位置尋找 multilingual candidate。</summary>
    private static string FindMultilingualModelRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LAYA_MULTILINGUAL_MODEL_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? FindRepositoryFile("models", "laya-multilingual", "staging")
            : configured;
    }

    /// <summary>從 test host base directory 回溯取得 repository 檔案或目錄。</summary>
    private static string FindRepositoryFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository path: {Path.Combine(parts)}");
    }
}
