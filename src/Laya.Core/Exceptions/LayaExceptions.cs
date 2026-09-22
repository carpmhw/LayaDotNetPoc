namespace Laya.Core.Exceptions;

/// <summary>保存 Laya 錯誤所需的診斷欄位，避免呼叫端只能解析文字訊息。</summary>
public abstract class LayaException : Exception
{
    /// <summary>建立包含階段與可選診斷欄位的 Laya 例外。</summary>
    protected LayaException(
        string message,
        string stage,
        string? modelPath = null,
        string? questionName = null,
        int? sequenceLength = null,
        int? optionCount = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Stage = stage;
        ModelPath = modelPath;
        QuestionName = questionName;
        SequenceLength = sequenceLength;
        OptionCount = optionCount;
    }

    /// <summary>取得發生錯誤的處理階段。</summary>
    public string Stage { get; }

    /// <summary>取得模型路徑；無法取得時為 null。</summary>
    public string? ModelPath { get; }

    /// <summary>取得相關問題名稱；無法取得時為 null。</summary>
    public string? QuestionName { get; }

    /// <summary>取得相關 sequence 長度；尚未建立時為 null。</summary>
    public int? SequenceLength { get; }

    /// <summary>取得相關 option 數量；尚未建立時為 null。</summary>
    public int? OptionCount { get; }
}

/// <summary>表示必要模型檔案不存在。</summary>
public sealed class LayaModelNotFoundException : LayaException
{
    /// <summary>建立缺少模型檔案的例外。</summary>
    public LayaModelNotFoundException(string modelPath, IReadOnlyList<string> missingFiles)
        : base(
            $"Model bundle is incomplete at '{modelPath}'. Missing: {string.Join(", ", missingFiles)}.",
            "model-validation",
            modelPath)
    {
        MissingFiles = missingFiles;
    }

    /// <summary>取得缺少的相對檔案路徑。</summary>
    public IReadOnlyList<string> MissingFiles { get; }
}

/// <summary>表示模型設定或輸入契約不符合要求。</summary>
public sealed class LayaConfigurationException : LayaException
{
    /// <summary>建立設定錯誤例外。</summary>
    public LayaConfigurationException(
        string message,
        string? modelPath = null,
        string? questionName = null,
        int? sequenceLength = null,
        int? optionCount = null,
        Exception? innerException = null)
        : base(
            message,
            "configuration",
            modelPath,
            questionName,
            sequenceLength,
            optionCount,
            innerException)
    {
    }
}

/// <summary>表示 tokenizer 載入或編碼失敗。</summary>
public sealed class LayaTokenizationException : LayaException
{
    /// <summary>建立 tokenizer 錯誤例外。</summary>
    public LayaTokenizationException(
        string message,
        string? modelPath = null,
        string? questionName = null,
        Exception? innerException = null)
        : base(message, "tokenization", modelPath, questionName, innerException: innerException)
    {
    }
}

/// <summary>表示輸入在 reference 規則下無法放入模型 context。</summary>
public sealed class LayaInputTooLongException : LayaException
{
    /// <summary>建立輸入長度例外。</summary>
    public LayaInputTooLongException(
        string message,
        int sequenceLength,
        int optionCount,
        string? questionName = null)
        : base(
            message,
            "sequence-building",
            questionName: questionName,
            sequenceLength: sequenceLength,
            optionCount: optionCount)
    {
    }
}

/// <summary>表示 ONNX Runtime 推論失敗。</summary>
public sealed class LayaInferenceException : LayaException
{
    /// <summary>建立推論錯誤例外。</summary>
    public LayaInferenceException(
        string message,
        string? modelPath = null,
        string? questionName = null,
        int? sequenceLength = null,
        int? optionCount = null,
        Exception? innerException = null)
        : base(
            message,
            "inference",
            modelPath,
            questionName,
            sequenceLength,
            optionCount,
            innerException)
    {
    }
}

/// <summary>表示校準或機率後處理失敗。</summary>
public sealed class LayaCalibrationException : LayaException
{
    /// <summary>建立校準錯誤例外。</summary>
    public LayaCalibrationException(
        string message,
        string? questionName = null,
        int? optionCount = null,
        Exception? innerException = null)
        : base(
            message,
            "calibration",
            questionName: questionName,
            optionCount: optionCount,
            innerException: innerException)
    {
    }
}
