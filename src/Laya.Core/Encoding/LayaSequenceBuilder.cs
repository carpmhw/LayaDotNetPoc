using System.Text.Encodings.Web;
using System.Text.Json;
using Laya.Core.Configuration;
using Laya.Core.Exceptions;
using Laya.Core.Models;
using Laya.Core.Serialization;
using Laya.Core.Tokenization;

namespace Laya.Core.Encoding;

/// <summary>保存單一 question 的完整 sequence 與 marker 對應。</summary>
public sealed class LayaBatchQuestion
{
    /// <summary>建立 batch question metadata。</summary>
    public LayaBatchQuestion(
        string name,
        LayaQuestionType type,
        IReadOnlyList<int> tokenIds,
        IReadOnlyList<int> markerPositions)
    {
        Name = name;
        Type = type;
        TokenIds = tokenIds;
        MarkerPositions = markerPositions;
    }

    /// <summary>取得 question name。</summary>
    public string Name { get; }

    /// <summary>取得 question type。</summary>
    public LayaQuestionType Type { get; }

    /// <summary>取得完整、未 padding 的 token IDs。</summary>
    public IReadOnlyList<int> TokenIds { get; }

    /// <summary>取得完整 sequence 中每個 option marker 的位置。</summary>
    public IReadOnlyList<int> MarkerPositions { get; }

    /// <summary>取得有效 option 數量。</summary>
    public int OptionCount => MarkerPositions.Count;

    /// <summary>取得未 padding 的 sequence 長度。</summary>
    public int SequenceLength => TokenIds.Count;
}

/// <summary>保存一次多 question batch 的五項 ONNX input tensors。</summary>
public sealed class LayaInputBatch
{
    /// <summary>建立 immutable batch snapshot。</summary>
    internal LayaInputBatch(
        int sequenceLength,
        int markerCount,
        long[] inputIds,
        bool[] attentionMask,
        long[] markerPositions,
        bool[] markerMask,
        long[] questionTypes,
        IReadOnlyList<LayaBatchQuestion> questions)
    {
        SequenceLength = sequenceLength;
        MarkerCount = markerCount;
        InputIds = inputIds;
        AttentionMask = attentionMask;
        MarkerPositions = markerPositions;
        MarkerMask = markerMask;
        QuestionTypes = questionTypes;
        Questions = questions;
    }

    /// <summary>取得 batch row 數量。</summary>
    public int BatchSize => Questions.Count;

    /// <summary>取得右側 padding 後的 sequence length。</summary>
    public int SequenceLength { get; }

    /// <summary>取得右側 padding 後的 marker width。</summary>
    public int MarkerCount { get; }

    /// <summary>取得 flattened int64 input_ids，shape 為 [B,L]。</summary>
    public long[] InputIds { get; }

    /// <summary>取得 flattened bool attention_mask，shape 為 [B,L]。</summary>
    public bool[] AttentionMask { get; }

    /// <summary>取得 flattened int64 marker_pos，shape 為 [B,K]。</summary>
    public long[] MarkerPositions { get; }

    /// <summary>取得 flattened bool marker_mask，shape 為 [B,K]。</summary>
    public bool[] MarkerMask { get; }

    /// <summary>取得 int64 qtype，shape 為 [B]。</summary>
    public long[] QuestionTypes { get; }

    /// <summary>取得每一 row 的原始 token 與 marker metadata。</summary>
    public IReadOnlyList<LayaBatchQuestion> Questions { get; }
}

/// <summary>依照 upstream build_sequence 與 collate_items 建立 Laya tensors。</summary>
public sealed class LayaSequenceBuilder
{
    private const int MaximumOptionTokens = 48;
    private static readonly JsonSerializerOptions InstructionJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly LayaTokenizer _tokenizer;
    private readonly LayaModelConfig _config;

    /// <summary>建立使用既有 tokenizer 與 model config 的 sequence builder。</summary>
    public LayaSequenceBuilder(LayaTokenizer tokenizer, LayaModelConfig config)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(config);

        if (config.MaxLength <= 0 || config.HeadMaxLength <= 0)
        {
            throw new LayaConfigurationException(
                "Sequence builder requires positive max_len and head_max_len.");
        }

        _tokenizer = tokenizer;
        _config = config;
    }

    /// <summary>建立單次 request 的右側 padding batch。</summary>
    public LayaInputBatch Build(LayaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Questions.Count == 0)
        {
            throw new LayaConfigurationException(
                "Laya request must contain at least one question.");
        }

        var questions = request.Questions
            .Select(question => BuildQuestion(question, request.State))
            .ToArray();
        var sequenceLength = questions.Max(question => question.TokenIds.Count);
        var markerCount = questions.Max(question => question.MarkerPositions.Count);
        var inputIds = Enumerable.Repeat((long)_tokenizer.PadTokenId, questions.Length * sequenceLength).ToArray();
        var attentionMask = new bool[questions.Length * sequenceLength];
        var markerPositions = new long[questions.Length * markerCount];
        var markerMask = new bool[questions.Length * markerCount];
        var questionTypes = new long[questions.Length];

        for (var row = 0; row < questions.Length; row++)
        {
            var question = questions[row];
            for (var token = 0; token < question.TokenIds.Count; token++)
            {
                inputIds[row * sequenceLength + token] = question.TokenIds[token];
                attentionMask[row * sequenceLength + token] = true;
            }

            for (var marker = 0; marker < question.MarkerPositions.Count; marker++)
            {
                markerPositions[row * markerCount + marker] = question.MarkerPositions[marker];
                markerMask[row * markerCount + marker] = true;
            }

            questionTypes[row] = (long)question.Type;
        }

        return new LayaInputBatch(
            sequenceLength,
            markerCount,
            inputIds,
            attentionMask,
            markerPositions,
            markerMask,
            questionTypes,
            questions);
    }

    /// <summary>建立單一 question 的 reference sequence 與 marker positions。</summary>
    private LayaBatchQuestion BuildQuestion(LayaQuestion question, object? state)
    {
        var optionTexts = RenderOptions(question);
        var instruction = Scrub(RenderInstructions(question.Instructions));
        var headIds = _tokenizer
            .Encode($"{GetTypeName(question.Type)} question: {instruction}")
            .ToList();
        var optionIds = optionTexts
            .Select(option =>
            {
                var ids = new List<int> { _tokenizer.MaskTokenId };
                ids.AddRange(_tokenizer.Encode(" " + Scrub(option)).Take(MaximumOptionTokens));
                return ids;
            })
            .ToList();

        var optionBudget = _config.HeadMaxLength - optionIds.Sum(ids => ids.Count);
        if (optionBudget < 16)
        {
            var perOption = Math.Max(
                4,
                (int)Math.Floor(
                    (_config.HeadMaxLength - 16) / (double)Math.Max(1, optionIds.Count)));
            optionIds = optionIds
                .Select(ids => ids.Take(perOption).ToList())
                .ToList();
            optionBudget = _config.HeadMaxLength - optionIds.Sum(ids => ids.Count);
        }

        headIds = headIds.Take(Math.Max(8, optionBudget)).ToList();
        var sequence = new List<int> { _tokenizer.ClsTokenId };
        sequence.AddRange(headIds);
        sequence.Add(_tokenizer.SepTokenId);
        var markerPositions = new List<int>(optionIds.Count);

        foreach (var option in optionIds)
        {
            markerPositions.Add(sequence.Count);
            sequence.AddRange(option);
        }

        sequence.Add(_tokenizer.SepTokenId);
        var stateRoom = Math.Max(0, _config.MaxLength - sequence.Count - 1);
        var stateIds = _tokenizer
            .Encode(Scrub(LayaStateSerializer.Serialize(state)))
            .Take(stateRoom);
        sequence.AddRange(stateIds);
        sequence.Add(_tokenizer.SepTokenId);

        var finalMarkers = markerPositions
            .Where(marker => marker < _config.MaxLength)
            .ToArray();
        if (finalMarkers.Length != optionTexts.Count)
        {
            throw new LayaInputTooLongException(
                $"Question '{question.Name}' options do not fit in head_max_len={_config.HeadMaxLength}.",
                sequence.Count,
                optionTexts.Count,
                question.Name);
        }

        return new LayaBatchQuestion(
            question.Name,
            question.Type,
            sequence.Take(_config.MaxLength).ToArray(),
            finalMarkers);
    }

    /// <summary>依照 upstream renderOptions 產生 option 文字。</summary>
    private static IReadOnlyList<string> RenderOptions(LayaQuestion question)
    {
        if (question.Type == LayaQuestionType.Noul)
        {
            return new[]
            {
                "false: no, the statement does not hold",
                "true: yes, the statement holds"
            };
        }

        if (question.Options.Count == 0 ||
            question.Options.Any(string.IsNullOrWhiteSpace) ||
            question.Options.Distinct(StringComparer.Ordinal).Count() != question.Options.Count)
        {
            throw new LayaConfigurationException(
                $"Question '{question.Name}' must contain non-empty unique options.",
                questionName: question.Name);
        }

        return question.Type switch
        {
            LayaQuestionType.Choice => question.PromptOptions,
            LayaQuestionType.Score => question.PromptOptions
                .Select((option, index) => $"level {index}: {option}")
                .ToArray(),
            _ => throw new LayaConfigurationException(
                $"Unknown question type '{question.Type}'.",
                questionName: question.Name)
        };
    }

    /// <summary>取得 reference 使用的小寫 question type name。</summary>
    private static string GetTypeName(LayaQuestionType type)
    {
        return type switch
        {
            LayaQuestionType.Choice => "choice",
            LayaQuestionType.Score => "score",
            LayaQuestionType.Noul => "noul",
            _ => throw new LayaConfigurationException($"Unknown question type '{type}'.")
        };
    }

    /// <summary>將 instructions 轉成 reference header 使用的文字。</summary>
    private static string RenderInstructions(object? instructions)
    {
        return instructions is string text
            ? text
            : JsonSerializer.Serialize(instructions, InstructionJsonOptions);
    }

    /// <summary>移除 instructions、options 與 state 中的 mask token 文字。</summary>
    private string Scrub(string value)
    {
        return value.Replace(_tokenizer.MaskToken, " ", StringComparison.Ordinal);
    }
}
