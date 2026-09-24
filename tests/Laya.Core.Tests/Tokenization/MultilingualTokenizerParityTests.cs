using System.Text.Json;
using Laya.Core.Tokenization;

namespace Laya.Core.Tests.Tokenization;

/// <summary>驗證 multilingual tokenizer 與官方 Python fixture 的離散契約。</summary>
[Trait("Category", "MultilingualTokenizer")]
public sealed class MultilingualTokenizerParityTests
{
    private const int ExpectedFixtureCount = 19;

    /// <summary>逐一核對固定十九字串與官方 special-token IDs。</summary>
    [Fact]
    public void Encode_MatchesAllOfficialMultilingualFixtures()
    {
        var modelRoot = FindMultilingualModelRoot();
        var fixturePath = FindRepositoryFile("test-data", "multilingual-tokenizer-fixtures.json");
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var tokenizerElement = document.RootElement.GetProperty("tokenizer");

        Assert.Equal(
            tokenizerElement.GetProperty("specialTokens").GetProperty("bos").GetProperty("id").GetInt32(),
            tokenizer.ClsTokenId);
        Assert.Equal(
            tokenizerElement.GetProperty("specialTokens").GetProperty("eos").GetProperty("id").GetInt32(),
            tokenizer.SepTokenId);
        Assert.Equal(
            tokenizerElement.GetProperty("specialTokens").GetProperty("pad").GetProperty("id").GetInt32(),
            tokenizer.PadTokenId);
        Assert.Equal(
            tokenizerElement.GetProperty("specialTokens").GetProperty("mask").GetProperty("id").GetInt32(),
            tokenizer.MaskTokenId);
        Assert.Equal(
            tokenizerElement.GetProperty("specialTokens").GetProperty("unk").GetProperty("id").GetInt32(),
            tokenizer.UnkTokenId);

        var fixtures = tokenizerElement.GetProperty("fixtures").EnumerateArray().ToArray();
        Assert.Equal(ExpectedFixtureCount, fixtures.Length);
        foreach (var fixture in fixtures)
        {
            var text = fixture.GetProperty("text").GetString()!;
            var expected = fixture.GetProperty("contentTokenIds")
                .EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray();
            var actual = tokenizer.Encode(text);
            Assert.Equal(expected, actual);

            if (text.Any(character => character is >= '\u4E00' and <= '\u9FFF'))
            {
                Assert.NotEmpty(actual);
                Assert.DoesNotContain(tokenizer.UnkTokenId, actual);
            }
        }

        var unknownFixture = tokenizerElement.GetProperty("nonWholeUnknownFixture");
        var unknownText = unknownFixture.GetProperty("text").GetString()!;
        var unknownExpected = unknownFixture.GetProperty("contentTokenIds")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        var unknownActual = tokenizer.Encode(unknownText).ToArray();
        Assert.Equal(unknownExpected, unknownActual);
        var unknownIndex = Array.IndexOf(unknownActual, tokenizer.UnkTokenId);
        Assert.InRange(unknownIndex, 1, unknownActual.Length - 2);
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
