using System.Text.Json;
using System.Text.Json.Serialization;
using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.PostProcessing;
using Laya.Core.Serialization;
using Laya.Core.Tokenization;

namespace Laya.Core.Tests.Parity;

public sealed class LayaParityFixtureTests
{
    /// <summary>驗證 reference fixtures 的 sequence、tensors、raw outputs 與 probabilities。</summary>
    [Fact]
    public void ReferenceFixtures_MatchDotNetImplementation()
    {
        var modelRoot = FindModelRoot();
        var fixturePath = FindFixturePath();
        var document = JsonSerializer.Deserialize<FixtureDocument>(
            File.ReadAllText(fixturePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.True(document.Fixtures.Count >= 10);
        using var session = LayaOnnxSession.Open(modelRoot);
        using var tokenizer = LayaTokenizer.Load(
            Path.Combine(modelRoot, "tokenizer", "tokenizer.json"),
            Path.Combine(modelRoot, "tokenizer", "tokenizer_config.json"));
        var config = LayaModelConfig.Load(Path.Combine(modelRoot, "laya_config.json"));
        var builder = new LayaSequenceBuilder(tokenizer, config);
        var runner = new LayaInferenceRunner(session);
        var processor = new LayaPostProcessor();

        foreach (var fixture in document.Fixtures)
        {
            var request = CreateRequest(fixture);
            Assert.Equal(fixture.SerializedState, LayaStateSerializer.Serialize(request.State));

            var batch = builder.Build(request);
            AssertSequence(fixture, batch);
            AssertBatch(fixture, batch);

            var inference = runner.Run(batch);
            AssertFloatArrays(fixture.Outputs.Logits, inference.Logits, tolerance: 1e-4);
            AssertFloatArrays(fixture.Outputs.ActProbabilities, inference.ActProbabilities, tolerance: 1e-4);

            var answers = processor.Process(request, batch, inference, config);
            AssertAnswers(fixture, answers, tolerance: 1e-4);
        }
    }

    /// <summary>由 fixture JSON 建立可供 .NET sequence builder 使用的 request。</summary>
    private static LayaRequest CreateRequest(Fixture fixture)
    {
        object? state = fixture.State.ValueKind == JsonValueKind.String
            ? fixture.State.GetString()
            : fixture.State.Clone();
        var questions = fixture.Questions
            .Select(question => new LayaQuestion(
                question.Name,
                ParseType(question.Type),
                question.Instructions.ValueKind == JsonValueKind.String
                    ? question.Instructions.GetString()
                    : question.Instructions.Clone(),
                question.Options))
            .ToArray();

        return new LayaRequest(state, questions);
    }

    /// <summary>將 reference type name 轉為固定 numeric enum。</summary>
    private static LayaQuestionType ParseType(string type)
    {
        return type switch
        {
            "choice" => LayaQuestionType.Choice,
            "score" => LayaQuestionType.Score,
            "noul" => LayaQuestionType.Noul,
            _ => throw new InvalidOperationException($"Unknown fixture question type '{type}'.")
        };
    }

    /// <summary>逐 question 比對未 padding sequence 與 marker positions。</summary>
    private static void AssertSequence(Fixture fixture, LayaInputBatch batch)
    {
        Assert.Equal(fixture.Sequences.Count, batch.Questions.Count);
        for (var index = 0; index < fixture.Sequences.Count; index++)
        {
            Assert.Equal(fixture.Sequences[index].TokenIds, batch.Questions[index].TokenIds);
            Assert.Equal(fixture.Sequences[index].MarkerPositions, batch.Questions[index].MarkerPositions);
            Assert.Equal(fixture.Sequences[index].Qtype, batch.QuestionTypes[index]);
        }
    }

    /// <summary>比對五項 collated tensors 的 shape 與每一個元素。</summary>
    private static void AssertBatch(Fixture fixture, LayaInputBatch batch)
    {
        Assert.Equal(fixture.Batch.Shapes.InputIds, new[] { batch.BatchSize, batch.SequenceLength });
        Assert.Equal(fixture.Batch.Shapes.MarkerPos, new[] { batch.BatchSize, batch.MarkerCount });
        Assert.Equal(fixture.Batch.InputIds, batch.InputIds.Select(value => (int)value));
        Assert.Equal(
            fixture.Batch.AttentionMask,
            batch.AttentionMask.Select(value => value ? 1 : 0));
        Assert.Equal(fixture.Batch.MarkerPos, batch.MarkerPositions.Select(value => (int)value));
        Assert.Equal(fixture.Batch.MarkerMask, batch.MarkerMask);
        Assert.Equal(fixture.Batch.Qtype, batch.QuestionTypes.Select(value => (int)value));
    }

    /// <summary>比對 reference raw float arrays，允許跨 runtime 的小幅浮點差異。</summary>
    private static void AssertFloatArrays(
        IReadOnlyList<float> expected,
        IReadOnlyList<float> actual,
        double tolerance)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.InRange(Math.Abs(expected[index] - actual[index]), 0d, tolerance);
        }
    }

    /// <summary>比對 calibrated probability 與 selected option 結果。</summary>
    private static void AssertAnswers(
        Fixture fixture,
        IReadOnlyList<LayaAnswer> actual,
        double tolerance)
    {
        Assert.Equal(fixture.Answers.Count, actual.Count);
        for (var row = 0; row < actual.Count; row++)
        {
            var expected = fixture.Answers[row];
            var answer = actual[row];
            Assert.Equal(expected.QuestionId, answer.Name);
            Assert.Equal(expected.Options.Count, answer.Probabilities.Count);

            for (var option = 0; option < expected.Options.Count; option++)
            {
                Assert.InRange(
                    Math.Abs(expected.Probabilities[option] - answer.Probabilities.Values.ElementAt(option)),
                    0d,
                    tolerance);
            }

            Assert.Equal(expected.SelectedIndex, expected.Probabilities.IndexOf(expected.Probabilities.Max()));
            var expectedOption = expected.Options[expected.SelectedIndex];
            if (expectedOption.StartsWith("false:", StringComparison.Ordinal) ||
                expectedOption.StartsWith("true:", StringComparison.Ordinal))
            {
                expectedOption = expected.SelectedIndex == 0 ? "false" : "true";
            }

            Assert.Equal(expectedOption, answer.SelectedOption);
        }
    }

    /// <summary>尋找本機 parity fixture 檔案。</summary>
    private static string FindFixturePath()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../test-data/parity-fixtures.json"));
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

    private sealed class FixtureDocument
    {
        /// <summary>取得所有 reference fixtures。</summary>
        public List<Fixture> Fixtures { get; init; } = new();
    }

    private sealed class Fixture
    {
        /// <summary>取得 fixture state。</summary>
        public JsonElement State { get; init; }

        /// <summary>取得 reference serialized state。</summary>
        public string SerializedState { get; init; } = string.Empty;

        /// <summary>取得 question inputs。</summary>
        public List<FixtureQuestion> Questions { get; init; } = new();

        /// <summary>取得 sequence traces。</summary>
        public List<FixtureSequence> Sequences { get; init; } = new();

        /// <summary>取得 collated tensors。</summary>
        public FixtureBatch Batch { get; init; } = new();

        /// <summary>取得 raw outputs。</summary>
        public FixtureOutputs Outputs { get; init; } = new();

        /// <summary>取得 reference answers。</summary>
        public List<FixtureAnswer> Answers { get; init; } = new();
    }

    private sealed class FixtureQuestion
    {
        /// <summary>取得 question name。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>取得 reference question type。</summary>
        public string Type { get; init; } = string.Empty;

        /// <summary>取得 instructions JSON value。</summary>
        public JsonElement Instructions { get; init; }

        /// <summary>取得 option labels。</summary>
        public List<string> Options { get; init; } = new();
    }

    private sealed class FixtureSequence
    {
        /// <summary>取得 question id。</summary>
        public string QuestionId { get; init; } = string.Empty;

        /// <summary>取得未 padding token IDs。</summary>
        public List<int> TokenIds { get; init; } = new();

        /// <summary>取得 marker positions。</summary>
        public List<int> MarkerPositions { get; init; } = new();

        /// <summary>取得 qtype numeric value。</summary>
        public int Qtype { get; init; }
    }

    private sealed class FixtureBatch
    {
        /// <summary>取得 batch shapes。</summary>
        public FixtureShapes Shapes { get; init; } = new();

        /// <summary>取得 input_ids。</summary>
        [JsonPropertyName("input_ids")]
        public List<int> InputIds { get; init; } = new();

        /// <summary>取得 attention_mask。</summary>
        [JsonPropertyName("attention_mask")]
        public List<int> AttentionMask { get; init; } = new();

        /// <summary>取得 marker_pos。</summary>
        [JsonPropertyName("marker_pos")]
        public List<int> MarkerPos { get; init; } = new();

        /// <summary>取得 marker_mask。</summary>
        [JsonPropertyName("marker_mask")]
        public List<bool> MarkerMask { get; init; } = new();

        /// <summary>取得 qtype。</summary>
        public List<int> Qtype { get; init; } = new();
    }

    private sealed class FixtureShapes
    {
        /// <summary>取得 input_ids shape。</summary>
        [JsonPropertyName("input_ids")]
        public List<int> InputIds { get; init; } = new();

        /// <summary>取得 marker_pos shape。</summary>
        [JsonPropertyName("marker_pos")]
        public List<int> MarkerPos { get; init; } = new();
    }

    private sealed class FixtureOutputs
    {
        /// <summary>取得 logits。</summary>
        public List<float> Logits { get; init; } = new();

        /// <summary>取得 act_probs。</summary>
        [JsonPropertyName("act_probs")]
        public List<float> ActProbabilities { get; init; } = new();
    }

    private sealed class FixtureAnswer
    {
        /// <summary>取得 question id。</summary>
        public string QuestionId { get; init; } = string.Empty;

        /// <summary>取得 option labels。</summary>
        public List<string> Options { get; init; } = new();

        /// <summary>取得 calibrated probabilities。</summary>
        public List<double> Probabilities { get; init; } = new();

        /// <summary>取得 reference selected index。</summary>
        public int SelectedIndex { get; init; }
    }
}
