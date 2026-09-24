using System.Security.Cryptography;
using System.Text.Json;
using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.PostProcessing;
using Laya.Core.Serialization;
using Laya.Core.Tokenization;

namespace Laya.Core.Tests.Parity;

/// <summary>以官方 Python fixture 驗證 multilingual .NET encoding、ORT 與 postprocess。</summary>
[Trait("Category", "MultilingualModel")]
public sealed class MultilingualDotnetParityTests
{
    private const int ExpectedFixtureCount = 20;
    private const double AbsoluteTolerance = 1e-4;
    private const string CheckpointRevision = "052592a15d198d9ad47da779604259b10b47b7aa";

    /// <summary>執行全部官方 fixtures 並保存逐筆 machine-readable parity report。</summary>
    [Fact]
    public void RunAllOfficialFixturesWithLayeredParity()
    {
        var modelRoot = FindMultilingualModelRoot();
        var fixturePath = FindRepositoryPath("test-data", "multilingual-parity-fixtures.json");
        var reportPath = Environment.GetEnvironmentVariable("LAYA_DOTNET_PARITY_REPORT")
            ?? FindRepositoryPath("reports", "multilingual-dotnet-parity.json");
        var fixtureResults = new List<Dictionary<string, object?>>();
        var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        using var bundleManifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(modelRoot, "laya-bundle-manifest.json")));
        try
        {
            var root = document.RootElement;
            Assert.Equal("complete", root.GetProperty("status").GetString());
            Assert.Equal("052592a15d198d9ad47da779604259b10b47b7aa",
                root.GetProperty("reference").GetProperty("revision").GetString());
            Assert.Equal(2, bundleManifest.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("multilingual", bundleManifest.RootElement.GetProperty("profile").GetString());
            Assert.Equal(
                CheckpointRevision,
                bundleManifest.RootElement.GetProperty("checkpoint").GetProperty("revision").GetString());
            Assert.Equal(
                root.GetProperty("reference").GetProperty("toolchainLockSha256").GetString(),
                bundleManifest.RootElement.GetProperty("reference").GetProperty("toolchainLockSha256").GetString());

            using var session = LayaOnnxSession.Open(new LayaOptions(
                modelRoot,
                "multilingual",
                CheckpointRevision,
                allowCandidateStaged: true));
            using var tokenizer = LayaTokenizer.Load(
                session.Bundle.TokenizerPath,
                session.Bundle.TokenizerConfigPath);
            var builder = new LayaSequenceBuilder(tokenizer, session.Bundle.Config);
            var runner = new LayaInferenceRunner(session);
            var postProcessor = new LayaPostProcessor();
            var fixtures = root.GetProperty("fixtures").EnumerateArray().ToArray();
            Assert.Equal(ExpectedFixtureCount, fixtures.Length);
            foreach (var fixture in fixtures)
            {
                fixtureResults.Add(RunFixture(fixture, builder, runner, postProcessor, session.Bundle.Config));
            }
        }
        finally
        {
            document.Dispose();
        }

        var failed = fixtureResults.Where(result => result["status"] is not "complete").ToArray();
        WriteReport(reportPath, modelRoot, fixturePath, fixtureResults, failed.Length == 0);
        Assert.True(failed.Length == 0,
            $"Multilingual .NET parity failed: {string.Join(", ", failed.Select(item => item["id"]))}");
    }

    /// <summary>執行單筆 fixture 並以最早差異層保存失敗，不中斷其餘分母。</summary>
    private static Dictionary<string, object?> RunFixture(
        JsonElement fixture,
        LayaSequenceBuilder builder,
        LayaInferenceRunner runner,
        LayaPostProcessor postProcessor,
        LayaModelConfig config)
    {
        var id = fixture.GetProperty("id").GetString()!;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["primaryGroup"] = fixture.GetProperty("primaryGroup").GetString(),
            ["status"] = "complete"
        };
        try
        {
            var request = CreateRequest(fixture);
            var trace = fixture.GetProperty("trace");
            var expectedSerialized = trace.GetProperty("serializedState").GetString();
            var actualSerialized = LayaStateSerializer.Serialize(request.State);
            RequireEqual("serialization", expectedSerialized, actualSerialized);

            var batch = builder.Build(request);
            CompareSequence(batch, trace.GetProperty("sequences"));
            CompareInputs(batch, trace.GetProperty("inputs"));
            var inference = runner.Run(batch);
            var rawErrors = CompareOutputs(inference, batch, trace.GetProperty("outputs"));
            var answers = postProcessor.Process(request, batch, inference, config);
            var probabilityErrors = CompareAnswers(request, answers, fixture.GetProperty("expected"));
            result["shapes"] = new
            {
                batch = batch.BatchSize,
                sequence = batch.SequenceLength,
                markers = batch.MarkerCount
            };
            result["maxRawAbsoluteError"] = rawErrors.MaxAbsolute;
            result["maxRawRelativeError"] = rawErrors.MaxRelative;
            result["maxProbabilityAbsoluteError"] = probabilityErrors.MaxAbsolute;
            result["maxProbabilityRelativeError"] = probabilityErrors.MaxRelative;
            result["firstFailureLayer"] = null;
        }
        catch (Exception exception)
        {
            result["status"] = "failed";
            result["firstFailureLayer"] = exception is ParityLayerException parity
                ? parity.Layer
                : "execution";
            result["error"] = exception.Message;
        }

        return result;
    }

    /// <summary>將 fixture question criteria 轉成保留答案 key 的 .NET request。</summary>
    private static LayaRequest CreateRequest(JsonElement fixture)
    {
        var stateElement = fixture.GetProperty("state");
        object state = stateElement.ValueKind == JsonValueKind.String
            ? stateElement.GetString()!
            : stateElement.Clone();
        var questions = new List<LayaQuestion>();
        foreach (var questionProperty in fixture.GetProperty("questions").EnumerateObject())
        {
            var value = questionProperty.Value;
            var type = value.GetProperty("type").GetString() switch
            {
                "choice" => LayaQuestionType.Choice,
                "noul" => LayaQuestionType.Noul,
                _ => throw new ParityLayerException("question", "Unsupported fixture question type.")
            };
            var instructionElement = value.GetProperty("instructions");
            object instruction = instructionElement.ValueKind == JsonValueKind.String
                ? instructionElement.GetString()!
                : instructionElement.Clone();
            if (type == LayaQuestionType.Choice)
            {
                var criteria = value.GetProperty("criteria").EnumerateObject()
                    .Select(item => new KeyValuePair<string, string>(item.Name, item.Value.GetString()!));
                questions.Add(LayaQuestion.FromCriteria(questionProperty.Name, type, instruction, criteria));
            }
            else
            {
                questions.Add(new LayaQuestion(questionProperty.Name, type, instruction));
            }
        }

        return new LayaRequest(state, questions);
    }

    /// <summary>核對每一 question 的完整 sequence 與 marker positions。</summary>
    private static void CompareSequence(LayaInputBatch batch, JsonElement sequences)
    {
        var expected = sequences.EnumerateArray().ToArray();
        RequireEqual("sequence", expected.Length, batch.Questions.Count);
        for (var row = 0; row < expected.Length; row++)
        {
            RequireSequenceEqual("sequence", expected[row].GetProperty("tokenIds"), batch.Questions[row].TokenIds);
            RequireSequenceEqual("sequence", expected[row].GetProperty("markerPositions"), batch.Questions[row].MarkerPositions);
            RequireEqual("sequence", expected[row].GetProperty("qtype").GetInt64(), (long)batch.Questions[row].Type);
        }
    }

    /// <summary>核對 fixture 保存的五項 input tensor flatten values 與 shape。</summary>
    private static void CompareInputs(LayaInputBatch batch, JsonElement inputs)
    {
        var shapes = inputs.GetProperty("shapes");
        RequireShape("inputs", shapes.GetProperty("input_ids"), batch.BatchSize, batch.SequenceLength);
        RequireShape("inputs", shapes.GetProperty("marker_pos"), batch.BatchSize, batch.MarkerCount);
        RequireSequenceEqual("inputs", inputs.GetProperty("input_ids"), batch.InputIds);
        RequireSequenceEqual("inputs", inputs.GetProperty("attention_mask"), batch.AttentionMask.Select(value => value ? 1L : 0L));
        RequireSequenceEqual("inputs", inputs.GetProperty("marker_pos"), batch.MarkerPositions);
        RequireSequenceEqual("inputs", inputs.GetProperty("marker_mask"), batch.MarkerMask);
        RequireSequenceEqual("inputs", inputs.GetProperty("qtype"), batch.QuestionTypes);
    }

    /// <summary>比較 raw logits 與 act_probs 的有限有效值並回傳誤差摘要。</summary>
    private static ErrorSummary CompareOutputs(
        LayaInferenceResult actual,
        LayaInputBatch batch,
        JsonElement outputs)
    {
        var expectedLogits = ReadDoubles(outputs.GetProperty("logits"));
        var expectedAct = ReadDoubles(outputs.GetProperty("act_probs"));
        var actualLogits = actual.Logits.Select(value => (double)value).ToArray();
        var actualAct = actual.ActProbabilities.Select(value => (double)value).ToArray();
        var summary = CompareArrays("logits", expectedLogits, actualLogits, batch.MarkerMask);
        var actSummary = CompareArrays("act_probs", expectedAct, actualAct, null);
        return summary.Merge(actSummary);
    }

    /// <summary>比較 postprocess 後的 option probabilities 與 selected answer。</summary>
    private static ErrorSummary CompareAnswers(
        LayaRequest request,
        IReadOnlyList<LayaAnswer> actual,
        JsonElement expected)
    {
        var summary = ErrorSummary.Empty;
        var expectedItems = expected.EnumerateArray().ToArray();
        RequireEqual("answers", expectedItems.Length, actual.Count);
        for (var row = 0; row < expectedItems.Length; row++)
        {
            var expectedItem = expectedItems[row];
            var answer = actual[row];
            var values = ReadDoubles(expectedItem.GetProperty("probabilities"));
            var actualValues = request.Questions[row].Type == LayaQuestionType.Noul
                ? new[] { answer.Probabilities["false"], answer.Probabilities["true"] }
                : request.Questions[row].Options.Select(option => answer.Probabilities[option]).ToArray();
            summary = summary.Merge(CompareArrays("probabilities", values, actualValues, null));
            if (request.Questions[row].Type == LayaQuestionType.Choice)
            {
                RequireEqual("answers", expectedItem.GetProperty("selectedOption").GetString(), answer.SelectedOption);
            }
            else
            {
                var expectedTrue = expectedItem.GetProperty("pTrue").GetDouble();
                RequireClose("probabilities", expectedTrue, answer.Probability);
            }
        }

        return summary;
    }

    /// <summary>比較一組 flatten 數值，僅排除有 fixture marker mask 證據的 padding。</summary>
    private static ErrorSummary CompareArrays(
        string layer,
        IReadOnlyList<double> expected,
        IReadOnlyList<double> actual,
        IReadOnlyList<bool>? validMask)
    {
        if (expected.Count != actual.Count)
        {
            throw new ParityLayerException(layer, $"Shape mismatch: expected {expected.Count}, actual {actual.Count}.");
        }

        var maxAbsolute = 0d;
        var maxRelative = 0d;
        var actualIndex = 0;
        for (var index = 0; index < expected.Count; index++)
        {
            if (validMask is not null && !validMask[index])
            {
                continue;
            }

            if (!double.IsFinite(expected[index]) || !double.IsFinite(actual[index]))
            {
                throw new ParityLayerException(layer, $"Non-finite value at index {index}.");
            }

            var absolute = Math.Abs(expected[index] - actual[index]);
            var relative = absolute / Math.Max(Math.Abs(expected[index]), 1e-12);
            maxAbsolute = Math.Max(maxAbsolute, absolute);
            maxRelative = Math.Max(maxRelative, relative);
            actualIndex = index;
            if (absolute > AbsoluteTolerance)
            {
                throw new ParityLayerException(
                    layer,
                    $"Absolute error {absolute:R} exceeds {AbsoluteTolerance:R} at index {index}.");
            }
        }

        return new ErrorSummary(maxAbsolute, maxRelative, actualIndex);
    }

    /// <summary>核對固定 tolerance 內的單一浮點值。</summary>
    private static void RequireClose(string layer, double expected, double actual)
    {
        if (!double.IsFinite(expected) || !double.IsFinite(actual) || Math.Abs(expected - actual) > AbsoluteTolerance)
        {
            throw new ParityLayerException(layer, $"Value mismatch: expected {expected:R}, actual {actual:R}.");
        }
    }

    /// <summary>核對固定整數或字串值並在第一個差異層停止該 fixture。</summary>
    private static void RequireEqual<T>(string layer, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new ParityLayerException(layer, $"Expected '{expected}', actual '{actual}'.");
        }
    }

    /// <summary>核對 JSON 整數陣列與實際序列。</summary>
    private static void RequireSequenceEqual<T>(string layer, JsonElement expected, IEnumerable<T> actual)
    {
        var expectedValues = expected.EnumerateArray().Select(value =>
            typeof(T) == typeof(bool) ? (T)(object)(value.GetBoolean()) :
            typeof(T) == typeof(long) ? (T)(object)value.GetInt64() :
            (T)(object)value.GetInt32()).ToArray();
        RequireSequenceEqual(layer, expectedValues, actual.ToArray());
    }

    /// <summary>核對兩個序列的長度與每個離散值。</summary>
    private static void RequireSequenceEqual<T>(string layer, IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new ParityLayerException(layer, "Discrete sequence values differ.");
        }
    }

    /// <summary>核對二維 tensor shape 的 batch 與寬度。</summary>
    private static void RequireShape(string layer, JsonElement expected, int batch, int width)
    {
        var shape = expected.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (shape.Length != 2 || shape[0] != batch || shape[1] != width)
        {
            throw new ParityLayerException(layer, $"Shape differs: expected [{string.Join(',', shape)}], actual [{batch},{width}].");
        }
    }

    /// <summary>讀取 JSON 浮點 flatten array。</summary>
    private static double[] ReadDoubles(JsonElement element)
    {
        return element.EnumerateArray().Select(value => value.GetDouble()).ToArray();
    }

    /// <summary>保存完整 report，避免將 manifest 自身 hash 放入被 hash 的 artifact。</summary>
    private static void WriteReport(
        string path,
        string modelRoot,
        string fixturePath,
        IReadOnlyList<Dictionary<string, object?>> fixtures,
        bool passed)
    {
        var manifestPath = Path.Combine(modelRoot, "laya-bundle-manifest.json");
        var report = new
        {
            schemaVersion = 1,
            status = passed ? "complete" : "failed",
            profile = "multilingual",
            checkpointRevision = CheckpointRevision,
            modelRoot,
            fixturePath,
            bundleManifestSha256 = File.Exists(manifestPath) ? Sha256File(manifestPath) : null,
            fixtureSha256 = File.Exists(fixturePath) ? Sha256File(fixturePath) : null,
            expectedCount = ExpectedFixtureCount,
            fixtureCount = fixtures.Count,
            executedCount = fixtures.Count,
            passedCount = fixtures.Count(item => item["status"] is "complete"),
            skippedCount = 0,
            absoluteTolerance = AbsoluteTolerance,
            fixtures
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>以串流 SHA-256 保存 bundle manifest 身分。</summary>
    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>依環境變數或 repository 相對位置尋找 multilingual candidate。</summary>
    private static string FindMultilingualModelRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LAYA_MULTILINGUAL_MODEL_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var repositoryRoot = FindRepositoryPath();
        var pointerPath = Path.Combine(repositoryRoot, "models", "laya-multilingual", "current-bundle.json");
        using var pointer = JsonDocument.Parse(File.ReadAllText(pointerPath));
        return Path.GetFullPath(pointer.RootElement.GetProperty("root").GetString()!, repositoryRoot);
    }

    /// <summary>從 test host base directory 回溯取得 repository 路徑。</summary>
    private static string FindRepositoryPath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = parts.Length == 0
                ? current.FullName
                : Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            var isRepositoryRoot = parts.Length == 0 &&
                File.Exists(Path.Combine(current.FullName, "LayaDotNetPoc.sln"));
            if (isRepositoryRoot ||
                (parts.Length > 0 && (File.Exists(candidate) || Directory.Exists(candidate))))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository path: {Path.Combine(parts)}");
    }

    /// <summary>標示 fixture 最早發生差異的 parity layer。</summary>
    private sealed class ParityLayerException : Exception
    {
        /// <summary>建立包含 layer 的 parity 差異。</summary>
        public ParityLayerException(string layer, string message)
            : base(message)
        {
            Layer = layer;
        }

        /// <summary>取得最早差異層名稱。</summary>
        public string Layer { get; }
    }

    /// <summary>保存一組數值比較的 absolute/relative error 摘要。</summary>
    private readonly record struct ErrorSummary(double MaxAbsolute, double MaxRelative, int LastIndex)
    {
        /// <summary>取得空的 error 摘要。</summary>
        public static ErrorSummary Empty => new(0, 0, 0);

        /// <summary>合併兩組 layer error。</summary>
        public ErrorSummary Merge(ErrorSummary other)
        {
            return new ErrorSummary(
                Math.Max(MaxAbsolute, other.MaxAbsolute),
                Math.Max(MaxRelative, other.MaxRelative),
                Math.Max(LastIndex, other.LastIndex));
        }
    }
}
