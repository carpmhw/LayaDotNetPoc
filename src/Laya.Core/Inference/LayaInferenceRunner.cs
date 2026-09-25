using System.Diagnostics;
using Laya.Core.Encoding;
using Laya.Core.Exceptions;
using Microsoft.ML.OnnxRuntime;

namespace Laya.Core.Inference;

/// <summary>保存單次 ONNX Run 的 managed raw output snapshot。</summary>
public sealed class LayaInferenceResult
{
    /// <summary>建立 raw output snapshot。</summary>
    public LayaInferenceResult(
        float[] logits,
        float[] actProbabilities,
        TimeSpan runDuration)
    {
        Logits = logits;
        ActProbabilities = actProbabilities;
        RunDuration = runDuration;
    }

    /// <summary>取得 flattened logits，shape 為 [B,K]。</summary>
    public float[] Logits { get; }

    /// <summary>取得 flattened act_probs，shape 為 [B,2]。</summary>
    public float[] ActProbabilities { get; }

    /// <summary>取得只涵蓋 native Run 的 duration。</summary>
    public TimeSpan RunDuration { get; }
}

/// <summary>將 LayaInputBatch 轉成 OrtValue 並執行單次 ONNX CPU Run。</summary>
public sealed class LayaInferenceRunner
{
    private static readonly string[] OutputNames = { "logits", "act_probs" };
    private readonly LayaOnnxSession _session;
    private readonly Func<LayaInputBatch, LayaInputTensorOwner> _createInputOwner;
    private readonly Func<
        RunOptions,
        IReadOnlyDictionary<string, OrtValue>,
        IReadOnlyCollection<string>,
        IDisposableReadOnlyCollection<OrtValue>> _run;

    /// <summary>建立使用既有長生命週期 session 的 runner。</summary>
    public LayaInferenceRunner(LayaOnnxSession session)
        : this(
            session,
            LayaInputTensorOwner.Create,
            (runOptions, inputs, outputNames) => session.Run(runOptions, inputs, outputNames))
    {
    }

    /// <summary>建立可替換 tensor owner 與 native Run 邊界的 runner。</summary>
    internal LayaInferenceRunner(
        LayaOnnxSession session,
        Func<LayaInputBatch, LayaInputTensorOwner> createInputOwner,
        Func<
            RunOptions,
            IReadOnlyDictionary<string, OrtValue>,
            IReadOnlyCollection<string>,
            IDisposableReadOnlyCollection<OrtValue>> run)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _createInputOwner = createInputOwner ?? throw new ArgumentNullException(nameof(createInputOwner));
        _run = run ?? throw new ArgumentNullException(nameof(run));
    }

    /// <summary>建立五項 OrtValue、執行一次 Run 並複製 managed outputs。</summary>
    public LayaInferenceResult Run(LayaInputBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ValidateBatch(batch);

        using var inputOwner = _createInputOwner(batch);
        using var runOptions = new RunOptions();

        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var outputs = _run(runOptions, inputOwner.Inputs, OutputNames);
            stopwatch.Stop();

            var outputValues = outputs.ToArray();
            if (outputValues.Length != OutputNames.Length)
            {
                throw new LayaInferenceException(
                    "ONNX Runtime returned an unexpected output count.",
                    _session.Bundle.ModelPath);
            }

            var logits = CopyFloatTensor(
                outputValues[0],
                batch.BatchSize,
                batch.MarkerCount,
                "logits");
            var actProbabilities = CopyFloatTensor(
                outputValues[1],
                batch.BatchSize,
                2,
                "act_probs");

            return new LayaInferenceResult(logits, actProbabilities, stopwatch.Elapsed);
        }
        catch (LayaException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LayaInferenceException(
                "ONNX Runtime inference failed.",
                _session.Bundle.ModelPath,
                innerException: exception);
        }
    }

    /// <summary>驗證 batch tensor 長度與非空 shape。</summary>
    private void ValidateBatch(LayaInputBatch batch)
    {
        if (batch.BatchSize <= 0 || batch.SequenceLength <= 0 || batch.MarkerCount <= 0)
        {
            throw new LayaInferenceException(
                "Inference batch must have positive batch, sequence and marker dimensions.",
                _session.Bundle.ModelPath);
        }

        if (batch.InputIds.Length != batch.BatchSize * batch.SequenceLength ||
            batch.AttentionMask.Length != batch.InputIds.Length ||
            batch.MarkerPositions.Length != batch.BatchSize * batch.MarkerCount ||
            batch.MarkerMask.Length != batch.MarkerPositions.Length ||
            batch.QuestionTypes.Length != batch.BatchSize)
        {
            throw new LayaInferenceException(
                "Inference batch tensor lengths do not match declared shapes.",
                _session.Bundle.ModelPath);
        }
    }

    /// <summary>驗證 output tensor shape 並複製 float32 native buffer。</summary>
    private float[] CopyFloatTensor(
        OrtValue output,
        int expectedBatchSize,
        int expectedWidth,
        string outputName)
    {
        if (!output.IsTensor)
        {
            throw new LayaInferenceException(
                $"ONNX output '{outputName}' is not a tensor.",
                _session.Bundle.ModelPath);
        }

        var shape = output.GetTensorTypeAndShape().Shape;
        if (shape.Length != 2 ||
            shape[0] != expectedBatchSize ||
            shape[1] != expectedWidth)
        {
            throw new LayaInferenceException(
                $"ONNX output '{outputName}' has unexpected shape [{string.Join(",", shape)}].",
                _session.Bundle.ModelPath);
        }

        var values = output.GetTensorDataAsSpan<float>().ToArray();
        if (values.Length != expectedBatchSize * expectedWidth)
        {
            throw new LayaInferenceException(
                $"ONNX output '{outputName}' has an unexpected element count.",
                _session.Bundle.ModelPath);
        }

        return values;
    }
}
