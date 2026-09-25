using Laya.Core.Encoding;
using Microsoft.ML.OnnxRuntime;

namespace Laya.Core.Inference;

/// <summary>擁有單一 batch 的五個 ONNX input OrtValue 及其 managed backing arrays。</summary>
internal sealed class LayaInputTensorOwner : IDisposable
{
    private readonly LayaInputBatch _batch;
    private readonly long[] _attentionMaskValues;
    private readonly IReadOnlyList<OrtValue> _values;
    private readonly Action<OrtValue> _disposeValue;
    private IReadOnlyDictionary<string, OrtValue>? _inputs;

    /// <summary>建立擁有 batch input tensors 的資源 scope。</summary>
    private LayaInputTensorOwner(
        LayaInputBatch batch,
        long[] attentionMaskValues,
        IReadOnlyList<OrtValue> values,
        IReadOnlyDictionary<string, OrtValue> inputs,
        Action<OrtValue> disposeValue)
    {
        _batch = batch;
        _attentionMaskValues = attentionMaskValues;
        _values = values;
        _inputs = inputs;
        _disposeValue = disposeValue;
    }

    /// <summary>取得目前有效的五項 ONNX inputs。</summary>
    public IReadOnlyDictionary<string, OrtValue> Inputs =>
        Volatile.Read(ref _inputs) ?? throw new ObjectDisposedException(nameof(LayaInputTensorOwner));

    /// <summary>由真實 batch 建立 ONNX Runtime input OrtValues。</summary>
    public static LayaInputTensorOwner Create(LayaInputBatch batch)
    {
        return Create(batch, CreateOrtValue, static value => value.Dispose());
    }

    /// <summary>建立 inputs，並在任何建立失敗時釋放已成功建立的 OrtValues。</summary>
    internal static LayaInputTensorOwner Create(
        LayaInputBatch batch,
        Func<Array, long[], OrtValue> createValue,
        Action<OrtValue> disposeValue)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(createValue);
        ArgumentNullException.ThrowIfNull(disposeValue);
        ValidateBatch(batch);

        var attentionMaskValues = batch.AttentionMask
            .Select(value => value ? 1L : 0L)
            .ToArray();
        var values = new List<OrtValue>(capacity: 5);

        try
        {
            var inputIds = AddValue(values, createValue, batch.InputIds, new long[] { batch.BatchSize, batch.SequenceLength });
            var attentionMask = AddValue(values, createValue, attentionMaskValues, new long[] { batch.BatchSize, batch.SequenceLength });
            var markerPositions = AddValue(values, createValue, batch.MarkerPositions, new long[] { batch.BatchSize, batch.MarkerCount });
            var markerMask = AddValue(values, createValue, batch.MarkerMask, new long[] { batch.BatchSize, batch.MarkerCount });
            var questionTypes = AddValue(values, createValue, batch.QuestionTypes, new long[] { batch.BatchSize });

            var inputs = new Dictionary<string, OrtValue>(StringComparer.Ordinal)
            {
                ["input_ids"] = inputIds,
                ["attention_mask"] = attentionMask,
                ["marker_pos"] = markerPositions,
                ["marker_mask"] = markerMask,
                ["qtype"] = questionTypes
            };

            return new LayaInputTensorOwner(
                batch,
                attentionMaskValues,
                values.ToArray(),
                inputs,
                disposeValue);
        }
        catch (Exception creationException)
        {
            var disposalExceptions = DisposeValues(values, disposeValue);
            if (disposalExceptions.Count > 0)
            {
                disposalExceptions.Insert(0, creationException);
                throw new AggregateException(
                    "Creating ONNX input tensors failed and one or more partially created values could not be disposed.",
                    disposalExceptions);
            }

            throw;
        }
    }

    /// <summary>重複呼叫時只釋放一次，並確保所有 native values 都嘗試 dispose。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _inputs, null) is null)
        {
            return;
        }

        var disposalExceptions = DisposeValues(_values, _disposeValue);
        GC.KeepAlive(_batch);
        GC.KeepAlive(_attentionMaskValues);

        if (disposalExceptions.Count == 1)
        {
            throw disposalExceptions[0];
        }

        if (disposalExceptions.Count > 1)
        {
            throw new AggregateException("One or more ONNX input tensors could not be disposed.", disposalExceptions);
        }
    }

    /// <summary>依陣列元素型別建立 OrtValue tensor。</summary>
    private static OrtValue CreateOrtValue(Array memory, long[] shape)
    {
        return memory switch
        {
            long[] values => OrtValue.CreateTensorValueFromMemory(values, shape),
            bool[] values => OrtValue.CreateTensorValueFromMemory(values, shape),
            _ => throw new ArgumentException("Unsupported ONNX input tensor element type.", nameof(memory))
        };
    }

    /// <summary>在配置 native values 前驗證 batch dimensions 與 backing arrays。</summary>
    private static void ValidateBatch(LayaInputBatch batch)
    {
        if (batch.BatchSize <= 0 || batch.SequenceLength <= 0 || batch.MarkerCount <= 0)
        {
            throw new ArgumentException("Inference batch dimensions must be positive.", nameof(batch));
        }

        if (batch.InputIds.Length != batch.BatchSize * batch.SequenceLength ||
            batch.AttentionMask.Length != batch.InputIds.Length ||
            batch.MarkerPositions.Length != batch.BatchSize * batch.MarkerCount ||
            batch.MarkerMask.Length != batch.MarkerPositions.Length ||
            batch.QuestionTypes.Length != batch.BatchSize)
        {
            throw new ArgumentException("Inference batch arrays do not match their declared shapes.", nameof(batch));
        }
    }

    /// <summary>建立並記錄一個 batch OrtValue，供失敗路徑集中釋放。</summary>
    private static OrtValue AddValue(
        ICollection<OrtValue> values,
        Func<Array, long[], OrtValue> createValue,
        Array memory,
        long[] shape)
    {
        var value = createValue(memory, shape)
            ?? throw new InvalidOperationException("The input tensor factory returned null.");
        values.Add(value);
        return value;
    }

    /// <summary>逐一釋放已建立的 values，收集錯誤以確保後續資源仍會處理。</summary>
    private static List<Exception> DisposeValues(
        IEnumerable<OrtValue> values,
        Action<OrtValue> disposeValue)
    {
        var exceptions = new List<Exception>();
        foreach (var value in values.Reverse())
        {
            try
            {
                disposeValue(value);
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }

        return exceptions;
    }
}
