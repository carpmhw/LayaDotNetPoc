using Laya.Core.Tokenization;

namespace Laya.Core.Tests.Tokenization;

[Trait("Category", "EnglishTokenizer")]
public sealed class LayaTokenizerTests
{
    /// <summary>驗證本機 tokenizer 可載入並從設定解析 special token IDs。</summary>
    [Fact]
    public void Load_ResolvesSpecialTokensAndEncodesWithoutSpecialTokens()
    {
        var modelRoot = FindModelRoot();
        var tokenizerPath = Path.Combine(modelRoot, "tokenizer", "tokenizer.json");
        var configPath = Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json");

        using var tokenizer = LayaTokenizer.Load(tokenizerPath, configPath);

        Assert.Equal(50281, tokenizer.ClsTokenId);
        Assert.Equal(50282, tokenizer.SepTokenId);
        Assert.Equal(50283, tokenizer.PadTokenId);
        Assert.Equal(50284, tokenizer.MaskTokenId);
        Assert.Equal(50280, tokenizer.UnkTokenId);
        Assert.NotEmpty(tokenizer.Encode("Hello, world!"));
    }

    /// <summary>驗證代表性英文、中文與混合文字的 token IDs 固定不變。</summary>
    [Fact]
    public void Encode_MatchesGoldenIds()
    {
        var modelRoot = FindModelRoot();
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));

        var cases = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["Hello world"] = [12092, 1533],
            ["Uber Eats Taiwan"] = [54, 589, 444, 1832, 17975],
            ["全家便利商店"] = [30423, 34091, 16800, 125, 37208, 30676, 217, 12613, 234],
            ["台灣大車隊"] = [5941, 110, 48078, 98, 15962, 40329, 221, 26335, 221],
            ["APPLE.COM/BILL"] = [18137, 1843, 15, 9507, 16, 6159, 2293],
            ["Hello 世界"] = [12092, 209, 42848, 45261]
        };

        foreach (var item in cases)
        {
            Assert.Equal(item.Value, tokenizer.Encode(item.Key));
        }
    }

    /// <summary>驗證缺少 special token 設定時會明確拒絕 tokenizer。</summary>
    [Fact]
    public void Load_RejectsMissingSpecialTokenConfig()
    {
        var modelRoot = FindModelRoot();
        var directory = Directory.CreateTempSubdirectory();
        var invalidConfigPath = Path.Combine(directory.FullName, "tokenizer_config.json");

        try
        {
            File.WriteAllText(invalidConfigPath, "{\"cls_token\":\"[CLS]\"}");

            Assert.Throws<Laya.Core.Exceptions.LayaTokenizationException>(() =>
                LayaTokenizer.Load(
                    Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
                    invalidConfigPath));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>尋找 repository 內的本機模型目錄，允許 CI 透過環境變數覆寫。</summary>
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
