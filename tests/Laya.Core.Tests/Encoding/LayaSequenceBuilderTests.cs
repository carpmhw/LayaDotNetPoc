using Laya.Core.Configuration;
using Laya.Core.Models;
using Laya.Core.Tokenization;
using Laya.Core.Encoding;

namespace Laya.Core.Tests.Encoding;

public sealed class LayaSequenceBuilderTests
{
    /// <summary>驗證 Choice／Noul 會以一次 batch 建立 marker 與 qtype tensors。</summary>
    [Fact]
    public void Build_MixesChoiceAndNoulWithRightPadding()
    {
        var modelRoot = FindModelRoot();
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        var builder = new LayaSequenceBuilder(tokenizer, config);
        var request = new LayaRequest(
            "merchant state",
            new[]
            {
                new LayaQuestion("category", LayaQuestionType.Choice, "Pick one", new[] { "food", "travel" }),
                new LayaQuestion("needs_review", LayaQuestionType.Noul, "Is this uncertain?")
            });

        var batch = builder.Build(request);

        Assert.Equal(2, batch.BatchSize);
        Assert.Equal(2, batch.MarkerCount);
        Assert.Equal(new long[] { 0, 2 }, batch.QuestionTypes);
        Assert.Equal(2, batch.Questions[0].OptionCount);
        Assert.Equal(2, batch.Questions[1].OptionCount);

        for (var row = 0; row < batch.Questions.Count; row++)
        {
            for (var markerIndex = 0;
                 markerIndex < batch.Questions[row].MarkerPositions.Count;
                 markerIndex++)
            {
                var marker = batch.Questions[row].MarkerPositions[markerIndex];
                Assert.Equal(
                    tokenizer.MaskTokenId,
                    batch.InputIds[row * batch.SequenceLength + marker]);
                Assert.True(batch.MarkerMask[row * batch.MarkerCount +
                    markerIndex]);
            }
        }

        Assert.Contains(false, batch.AttentionMask);
    }

    /// <summary>驗證 Choice sequence 的 token trace 與 marker positions。</summary>
    [Fact]
    public void Build_MatchesChoiceGoldenTrace()
    {
        var modelRoot = FindModelRoot();
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        var batch = new LayaSequenceBuilder(tokenizer, config).Build(
            new LayaRequest(
                "merchant state",
                new[]
                {
                    new LayaQuestion("category", LayaQuestionType.Choice, "Pick one", new[] { "food", "travel" })
                }));

        Assert.Equal(
            new[]
            {
                50281, 22122, 1953, 27, 20745, 581, 50282, 50284, 2739,
                50284, 4288, 50282, 961, 22442, 1375, 50282
            },
            batch.Questions[0].TokenIds);
        Assert.Equal(new[] { 7, 9 }, batch.Questions[0].MarkerPositions);
    }

    /// <summary>驗證 Noul 的固定 false／true options 與 sequence trace。</summary>
    [Fact]
    public void Build_MatchesNoulGoldenTrace()
    {
        var modelRoot = FindModelRoot();
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        var batch = new LayaSequenceBuilder(tokenizer, config).Build(
            new LayaRequest(
                "merchant state",
                new[]
                {
                    new LayaQuestion("needs_review", LayaQuestionType.Noul, "Is this uncertain?")
                }));

        Assert.Equal(
            new[]
            {
                50281, 79, 3941, 1953, 27, 1680, 436, 8767, 32, 50282,
                50284, 3221, 27, 642, 13, 253, 3908, 1057, 417, 2186,
                50284, 2032, 27, 4754, 13, 253, 3908, 6556, 50282,
                961, 22442, 1375, 50282
            },
            batch.Questions[0].TokenIds);
        Assert.Equal(new[] { 10, 20 }, batch.Questions[0].MarkerPositions);
    }

    /// <summary>驗證長 state 只截斷 state，仍保留完整 header、options 與 markers。</summary>
    [Fact]
    public void Build_TruncatesStateToMaxLength()
    {
        var modelRoot = FindModelRoot();
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = new LayaModelConfig { MaxLength = 32, HeadMaxLength = 16 };
        var batch = new LayaSequenceBuilder(tokenizer, config).Build(
            new LayaRequest(
                new string('x', 256),
                new[]
                {
                    new LayaQuestion("category", LayaQuestionType.Choice, "Pick", new[] { "a", "b" })
                }));

        Assert.Equal(32, batch.Questions[0].SequenceLength);
        Assert.Equal(2, batch.Questions[0].MarkerPositions.Count);
        Assert.Equal(tokenizer.SepTokenId, batch.Questions[0].TokenIds[^1]);
    }

    /// <summary>驗證必要 options 無法放入 model context 時會明確失敗。</summary>
    [Fact]
    public void Build_RejectsOptionsThatExceedModelContext()
    {
        var modelRoot = FindModelRoot();
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = new LayaModelConfig { MaxLength = 32, HeadMaxLength = 16 };
        var options = Enumerable.Range(0, 32).Select(index => $"option-{index}");

        Assert.Throws<Laya.Core.Exceptions.LayaInputTooLongException>(() =>
            new LayaSequenceBuilder(tokenizer, config).Build(
                new LayaRequest(
                    "state",
                    new[]
                    {
                        new LayaQuestion("category", LayaQuestionType.Choice, "Pick", options)
                    })));
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
