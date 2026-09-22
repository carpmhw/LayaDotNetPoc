using System.Collections.ObjectModel;

namespace Laya.Core.Models;

/// <summary>對應 Laya export 的 question type numeric values。</summary>
public enum LayaQuestionType
{
    /// <summary>從離散選項中選出一個答案。</summary>
    Choice = 0,

    /// <summary>目前保留給 reference score 題型。</summary>
    Score = 1,

    /// <summary>判斷 statement 是否成立。</summary>
    Noul = 2
}

/// <summary>保存單一 Laya question 的輸入契約。</summary>
public sealed class LayaQuestion
{
    /// <summary>建立 question 並複製 option 集合。</summary>
    public LayaQuestion(
        string name,
        LayaQuestionType type,
        object? instructions,
        IEnumerable<string>? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Type = type;
        Instructions = instructions;
        Options = new ReadOnlyCollection<string>((options ?? Array.Empty<string>()).ToArray());
    }

    /// <summary>取得呼叫端使用的 question name。</summary>
    public string Name { get; }

    /// <summary>取得 question type。</summary>
    public LayaQuestionType Type { get; }

    /// <summary>取得原始 instructions；可為 string 或 JSON-like object。</summary>
    public object? Instructions { get; }

    /// <summary>取得 Choice／Score 的 option labels。</summary>
    public IReadOnlyList<string> Options { get; }
}

/// <summary>保存一次 state 與多個 questions 的 request。</summary>
public sealed class LayaRequest
{
    /// <summary>建立 request 並複製 question 順序。</summary>
    public LayaRequest(object? state, IEnumerable<LayaQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        State = state;
        Questions = new ReadOnlyCollection<LayaQuestion>(questions.ToArray());
    }

    /// <summary>取得待序列化的 state。</summary>
    public object? State { get; }

    /// <summary>取得依呼叫端順序保存的 questions。</summary>
    public IReadOnlyList<LayaQuestion> Questions { get; }
}

/// <summary>保存單一 question 的後處理答案。</summary>
public sealed class LayaAnswer
{
    /// <summary>建立答案並複製完整 probability distribution。</summary>
    public LayaAnswer(
        string name,
        string? selectedOption,
        double probability,
        IReadOnlyDictionary<string, double> probabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(probabilities);

        Name = name;
        SelectedOption = selectedOption;
        Probability = probability;
        Probabilities = new ReadOnlyDictionary<string, double>(
            new Dictionary<string, double>(probabilities, StringComparer.Ordinal));
    }

    /// <summary>取得 question name。</summary>
    public string Name { get; }

    /// <summary>取得選中的 option；尚未決定時為 null。</summary>
    public string? SelectedOption { get; }

    /// <summary>取得 selected option probability。</summary>
    public double Probability { get; }

    /// <summary>取得完整 option probability distribution。</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; }
}

/// <summary>保存一次決策的所有答案與 ONNX Run-only duration。</summary>
public sealed class LayaResult
{
    /// <summary>建立結果並複製答案順序。</summary>
    public LayaResult(IEnumerable<LayaAnswer> answers, TimeSpan inferenceDuration)
    {
        ArgumentNullException.ThrowIfNull(answers);

        Answers = new ReadOnlyCollection<LayaAnswer>(answers.ToArray());
        InferenceDuration = inferenceDuration;
    }

    /// <summary>取得依 request question 順序排列的答案。</summary>
    public IReadOnlyList<LayaAnswer> Answers { get; }

    /// <summary>取得只涵蓋 ONNX Run 的 duration。</summary>
    public TimeSpan InferenceDuration { get; }
}
