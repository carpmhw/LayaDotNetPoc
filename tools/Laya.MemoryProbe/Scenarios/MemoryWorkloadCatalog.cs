using System.Security.Cryptography;
using System.Text.Json;
using Laya.Core.Inference;
using Laya.Core.Models;

namespace Laya.MemoryProbe.Scenarios;

/// <summary>保存固定 tokenizer 輸入的識別碼、語言與文字。</summary>
internal sealed class MemoryTokenizerInput
{
    /// <summary>建立 tokenizer input fixture。</summary>
    public MemoryTokenizerInput(string id, string language, string text)
    {
        Id = id;
        Language = language;
        Text = text;
    }

    /// <summary>取得固定 fixture ID。</summary>
    public string Id { get; }

    /// <summary>取得語言標籤。</summary>
    public string Language { get; }

    /// <summary>取得 tokenizer 輸入文字。</summary>
    public string Text { get; }
}

/// <summary>保存 request ID 與可重複執行的 integrity Laya request。</summary>
internal sealed class MemoryIntegrityRequest
{
    /// <summary>建立單一 integrity request fixture。</summary>
    public MemoryIntegrityRequest(string id, LayaRequest request)
    {
        Id = id;
        Request = request;
    }

    /// <summary>取得 request 的穩定識別碼。</summary>
    public string Id { get; }

    /// <summary>取得待執行 request。</summary>
    public LayaRequest Request { get; }
}

/// <summary>載入固定 multilingual workload fixture 並建立可重現 Laya request。</summary>
internal sealed class MemoryWorkloadCatalog : IDisposable
{
    private JsonDocument? _workloadDocument;
    private JsonDocument? _manifestDocument;

    /// <summary>建立持有已驗證 workload 與 manifest JSON 文件的 catalog。</summary>
    private MemoryWorkloadCatalog(JsonDocument workloadDocument, JsonDocument manifestDocument)
    {
        _workloadDocument = workloadDocument;
        _manifestDocument = manifestDocument;
    }

    /// <summary>依 sidecar manifest SHA-256 驗證並載入 workload fixture。</summary>
    public static MemoryWorkloadCatalog Load(string workloadPath, string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (!File.Exists(workloadPath))
        {
            throw new FileNotFoundException($"Memory workload fixture was not found: {workloadPath}", workloadPath);
        }

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Memory workload manifest was not found: {manifestPath}", manifestPath);
        }

        JsonDocument? workloadDocument = null;
        JsonDocument? manifestDocument = null;
        try
        {
            manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = manifestDocument.RootElement;
            if (manifest.GetProperty("schemaVersion").GetInt32() != 1 ||
                !string.Equals(manifest.GetProperty("status").GetString(), "ready", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Memory workload manifest '{manifestPath}' is not ready schema version 1.");
            }

            var expectedHash = manifest.GetProperty("workload").GetProperty("sha256").GetString();
            using (var stream = File.OpenRead(workloadPath))
            {
                var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Memory workload fixture '{workloadPath}' does not match its manifest SHA-256.");
                }
            }

            workloadDocument = JsonDocument.Parse(File.ReadAllText(workloadPath));
            var workload = workloadDocument.RootElement;
            if (workload.GetProperty("schemaVersion").GetInt32() != 1 ||
                !string.Equals(workload.GetProperty("status").GetString(), "ready", StringComparison.Ordinal) ||
                !string.Equals(workload.GetProperty("profile").GetString(), "multilingual", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Memory workload fixture '{workloadPath}' has an unsupported identity.");
            }

            var catalog = new MemoryWorkloadCatalog(workloadDocument, manifestDocument);
            workloadDocument = null;
            manifestDocument = null;
            return catalog;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Memory workload fixture or manifest contains invalid JSON.", exception);
        }
        finally
        {
            workloadDocument?.Dispose();
            manifestDocument?.Dispose();
        }
    }

    /// <summary>依四欄交易 state profile 與 question count 建立 Laya request。</summary>
    public LayaRequest CreateRequest(string stateProfile, int questionCount)
    {
        var root = GetWorkloadRoot();
        if (stateProfile is not ("short" or "medium" or "long"))
        {
            throw new ArgumentException("State profile must be short, medium or long.", nameof(stateProfile));
        }

        var profile = root.GetProperty("stateProfiles").GetProperty(stateProfile);
        var phrase = ReadRequiredString(profile, "descriptionPhrase");
        var repeatCount = profile.GetProperty("descriptionRepeatCount").GetInt32();
        if (repeatCount <= 0)
        {
            throw new InvalidDataException($"State profile '{stateProfile}' has a non-positive description repeat count.");
        }

        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["description"] = string.Join(' ', Enumerable.Repeat(phrase, repeatCount)),
            ["amount"] = profile.GetProperty("amount").GetDouble(),
            ["currency"] = ReadRequiredString(profile, "currency"),
            ["direction"] = ReadRequiredString(profile, "direction")
        };
        var questionSetName = questionCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!root.GetProperty("questions").GetProperty("sets").TryGetProperty(questionSetName, out var questionSet))
        {
            throw new ArgumentOutOfRangeException(nameof(questionCount), "Question count must be 1, 2 or 5.");
        }

        return new LayaRequest(state, ReadQuestions(root, questionSet));
    }

    /// <summary>取得固定英文、中文及中英混合 tokenizer fixtures。</summary>
    public IReadOnlyList<MemoryTokenizerInput> GetTokenizerInputs()
    {
        return GetWorkloadRoot().GetProperty("tokenizerInputs")
            .EnumerateArray()
            .Select(input => new MemoryTokenizerInput(
                ReadRequiredString(input, "id"),
                ReadRequiredString(input, "language"),
                ReadRequiredString(input, "text")))
            .ToArray();
    }

    /// <summary>依固定 fixture ID 取得單一 tokenizer input。</summary>
    public MemoryTokenizerInput GetTokenizerInput(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return GetTokenizerInputs().FirstOrDefault(input => string.Equals(input.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown tokenizer workload '{id}'.", nameof(id));
    }

    /// <summary>取得固定 calibration fixture 的識別碼。</summary>
    public string GetCalibrationFixtureId()
    {
        return ReadRequiredString(GetWorkloadRoot().GetProperty("calibrationFixture"), "id");
    }

    /// <summary>建立官方 multilingual parity fixture 對應的 calibration request。</summary>
    public LayaRequest CreateCalibrationRequest()
    {
        var root = GetWorkloadRoot();
        var fixture = root.GetProperty("calibrationFixture");
        var state = ReadState(fixture.GetProperty("state"));
        return new LayaRequest(state, ReadQuestions(root, fixture.GetProperty("questions")));
    }

    /// <summary>建立與固定 calibration request 對應的 managed raw output snapshot。</summary>
    public LayaInferenceResult CreateCalibrationResult()
    {
        var fixture = GetWorkloadRoot().GetProperty("calibrationFixture");
        var logits = fixture.GetProperty("logits").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var actProbabilities = fixture.GetProperty("actProbabilities")
            .EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        return new LayaInferenceResult(logits, actProbabilities, TimeSpan.Zero);
    }

    /// <summary>建立四筆固定且具唯一 ID 的 concurrency integrity requests。</summary>
    public IReadOnlyList<MemoryIntegrityRequest> GetIntegrityRequests()
    {
        var root = GetWorkloadRoot();
        return root.GetProperty("integrityRequests")
            .EnumerateArray()
            .Select(item =>
            {
                var id = ReadRequiredString(item, "id");
                var state = ReadState(item.GetProperty("state"));
                var questionSetName = ReadRequiredString(item, "questionSet");
                var questions = root.GetProperty("questions").GetProperty("sets").GetProperty(questionSetName);
                return new MemoryIntegrityRequest(id, new LayaRequest(state, ReadQuestions(root, questions)));
            })
            .ToArray();
    }

    /// <summary>釋放持有的 JSON 文件，重複呼叫不會重複釋放。</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _workloadDocument, null)?.Dispose();
        Interlocked.Exchange(ref _manifestDocument, null)?.Dispose();
    }

    /// <summary>依 fixture question definition 建立 Choice 或 Noul question。</summary>
    private static IReadOnlyList<LayaQuestion> ReadQuestions(JsonElement root, JsonElement definitions)
    {
        var prompts = root.GetProperty("questions");
        var questions = new List<LayaQuestion>();
        foreach (var definition in definitions.EnumerateArray())
        {
            var name = ReadRequiredString(definition, "name");
            var type = ReadRequiredString(definition, "type") switch
            {
                "choice" => LayaQuestionType.Choice,
                "noul" => LayaQuestionType.Noul,
                _ => throw new InvalidDataException($"Question '{name}' has an unsupported type.")
            };
            var promptReference = ReadRequiredString(definition, "prompt");
            var prompt = prompts.TryGetProperty(promptReference, out var referencedPrompt)
                ? referencedPrompt.GetString()
                : promptReference;

            if (definition.TryGetProperty("criteria", out var criteria))
            {
                var entries = criteria.EnumerateObject()
                    .Select(item => new KeyValuePair<string, string>(item.Name, item.Value.GetString()!));
                questions.Add(LayaQuestion.FromCriteria(name, type, prompt, entries));
                continue;
            }

            IReadOnlyList<string>? options = null;
            if (definition.TryGetProperty("options", out var optionValue))
            {
                if (optionValue.ValueKind == JsonValueKind.String)
                {
                    options = prompts.GetProperty(optionValue.GetString()!)
                        .EnumerateArray()
                        .Select(option => option.GetString()!)
                        .ToArray();
                }
                else if (optionValue.ValueKind == JsonValueKind.Array)
                {
                    options = optionValue.EnumerateArray().Select(option => option.GetString()!).ToArray();
                }
            }

            questions.Add(new LayaQuestion(name, type, prompt, options));
        }

        if (questions.Count == 0)
        {
            throw new InvalidDataException("Memory workload question set cannot be empty.");
        }

        return questions;
    }

    /// <summary>取得已載入且仍有效的 workload JSON root。</summary>
    private JsonElement GetWorkloadRoot()
    {
        return Volatile.Read(ref _workloadDocument)?.RootElement
            ?? throw new ObjectDisposedException(nameof(MemoryWorkloadCatalog));
    }

    /// <summary>取得必要且非空的 JSON 字串欄位。</summary>
    private static string ReadRequiredString(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Memory workload property '{name}' is empty.");
    }

    /// <summary>將 JSON object 依原始 property order 複製成 serializer 相容 dictionary。</summary>
    private static Dictionary<string, object?> ReadState(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Memory workload state must be a JSON object.");
        }

        var state = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            state.Add(property.Name, ReadStateValue(property.Value));
        }

        return state;
    }

    /// <summary>複製 workload state 的 JSON primitive，保留整數與浮點數型別。</summary>
    private static object? ReadStateValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array or JsonValueKind.Object => value.Clone(),
            _ => throw new InvalidDataException($"Unsupported memory workload state kind '{value.ValueKind}'.")
        };
    }
}
