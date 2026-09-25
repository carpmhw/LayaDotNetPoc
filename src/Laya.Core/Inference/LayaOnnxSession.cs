using System.Collections.ObjectModel;
using System.Diagnostics;
using Laya.Core.Configuration;
using Laya.Core.Exceptions;
using Microsoft.ML.OnnxRuntime;

namespace Laya.Core.Inference;

/// <summary>保存 ONNX tensor 的名稱、元素型別與模型宣告 shape。</summary>
public sealed class LayaTensorMetadata
{
    /// <summary>建立單一 tensor metadata snapshot。</summary>
    public LayaTensorMetadata(
        string name,
        Type elementType,
        IReadOnlyList<long> dimensions)
    {
        Name = name;
        ElementType = elementType;
        Dimensions = dimensions;
    }

    /// <summary>取得 tensor 名稱。</summary>
    public string Name { get; }

    /// <summary>取得 tensor 元素型別。</summary>
    public Type ElementType { get; }

    /// <summary>取得 tensor 維度；負值表示動態維度。</summary>
    public IReadOnlyList<long> Dimensions { get; }

    /// <summary>取得 tensor rank。</summary>
    public int Rank => Dimensions.Count;
}

/// <summary>擁有單一長生命週期 CPU ONNX Runtime session。</summary>
public sealed class LayaOnnxSession : IDisposable
{
    private static readonly IReadOnlyDictionary<string, TensorContract> RequiredInputs =
        new Dictionary<string, TensorContract>(StringComparer.Ordinal)
        {
            ["input_ids"] = new(typeof(long), rank: 2),
            ["attention_mask"] = new(typeof(long), rank: 2),
            ["marker_pos"] = new(typeof(long), rank: 2),
            ["marker_mask"] = new(typeof(bool), rank: 2),
            ["qtype"] = new(typeof(long), rank: 1)
        };

    private static readonly IReadOnlyDictionary<string, TensorContract> RequiredOutputs =
        new Dictionary<string, TensorContract>(StringComparer.Ordinal)
        {
            ["logits"] = new(typeof(float), rank: 2),
            ["act_probs"] = new(
                typeof(float),
                rank: 2,
                dimensions: new long?[] { null, 2 })
        };

    private InferenceSession? _session;

    /// <summary>建立已初始化的 Laya ONNX session wrapper。</summary>
    private LayaOnnxSession(
        InferenceSession session,
        LayaModelBundle bundle,
        IReadOnlyDictionary<string, LayaTensorMetadata> inputMetadata,
        IReadOnlyDictionary<string, LayaTensorMetadata> outputMetadata,
        TimeSpan loadDuration)
    {
        _session = session;
        Bundle = bundle;
        InputMetadata = inputMetadata;
        OutputMetadata = outputMetadata;
        LoadDuration = loadDuration;
    }

    /// <summary>取得已驗證的 model bundle。</summary>
    public LayaModelBundle Bundle { get; }

    /// <summary>取得 input metadata snapshot。</summary>
    public IReadOnlyDictionary<string, LayaTensorMetadata> InputMetadata { get; }

    /// <summary>取得 output metadata snapshot。</summary>
    public IReadOnlyDictionary<string, LayaTensorMetadata> OutputMetadata { get; }

    /// <summary>取得 ONNX Runtime 建立 session 所花費的時間。</summary>
    public TimeSpan LoadDuration { get; }

    /// <summary>由模型根目錄驗證資產並建立 CPU session。</summary>
    public static LayaOnnxSession Open(string modelRoot)
    {
        return Open(new LayaOptions(modelRoot));
    }

    /// <summary>由指定選項驗證資產並建立 CPU session。</summary>
    public static LayaOnnxSession Open(LayaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var bundle = LayaModelValidator.ValidateBundle(options.ModelRoot, options.ManifestPath, options);
        SessionOptions? sessionOptions = null;
        InferenceSession? session = null;

        try
        {
            sessionOptions = CreateSessionOptions(options.EnableCpuMemArena);
            var loadStopwatch = Stopwatch.StartNew();
            session = new InferenceSession(bundle.ModelPath, sessionOptions);
            loadStopwatch.Stop();

            var inputMetadata = SnapshotMetadata(
                session.InputMetadata,
                "input",
                bundle.ModelPath);
            var outputMetadata = SnapshotMetadata(
                session.OutputMetadata,
                "output",
                bundle.ModelPath);
            ValidateSchema(inputMetadata, outputMetadata, bundle.ModelPath);

            var wrapper = new LayaOnnxSession(
                session,
                bundle,
                inputMetadata,
                outputMetadata,
                loadStopwatch.Elapsed);
            session = null;
            return wrapper;
        }
        catch (LayaException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LayaInferenceException(
                $"Could not initialize ONNX session for '{bundle.ModelPath}'.",
                bundle.ModelPath,
                innerException: exception);
        }
        finally
        {
            session?.Dispose();
            sessionOptions?.Dispose();
        }
    }

    /// <summary>取得供 Core inference pipeline 使用的原生 session。</summary>
    internal InferenceSession OrtSession => _session ?? throw new ObjectDisposedException(nameof(LayaOnnxSession));

    /// <summary>建立固定 CPU execution provider 與指定 memory arena 狀態的 session options。</summary>
    internal static SessionOptions CreateSessionOptions(bool enableCpuMemArena)
    {
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableCpuMemArena = enableCpuMemArena
        };

        try
        {
            sessionOptions.AppendExecutionProvider_CPU(enableCpuMemArena ? 1 : 0);
            return sessionOptions;
        }
        catch
        {
            sessionOptions.Dispose();
            throw;
        }
    }

    /// <summary>以指定 input OrtValues 執行一次 native Run。</summary>
    internal IDisposableReadOnlyCollection<OrtValue> Run(
        RunOptions runOptions,
        IReadOnlyDictionary<string, OrtValue> inputs,
        IReadOnlyCollection<string> outputNames)
    {
        ArgumentNullException.ThrowIfNull(runOptions);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputNames);

        return OrtSession.Run(runOptions, inputs, outputNames);
    }

    /// <summary>釋放 ONNX Runtime native session；重複呼叫不會重複釋放。</summary>
    public void Dispose()
    {
        var session = Interlocked.Exchange(ref _session, null);
        session?.Dispose();
    }

    /// <summary>將 ONNX Runtime metadata 複製成不依賴 native session 的 snapshot。</summary>
    private static IReadOnlyDictionary<string, LayaTensorMetadata> SnapshotMetadata(
        IReadOnlyDictionary<string, NodeMetadata> metadata,
        string direction,
        string modelPath)
    {
        var snapshots = new Dictionary<string, LayaTensorMetadata>(StringComparer.Ordinal);

        foreach (var item in metadata)
        {
            if (!item.Value.IsTensor)
            {
                throw new LayaConfigurationException(
                    $"ONNX {direction} '{item.Key}' is not a tensor.",
                    modelPath);
            }

            var dimensions = item.Value.Dimensions
                .Select(dimension => (long)dimension)
                .ToArray();
            snapshots.Add(
                item.Key,
                new LayaTensorMetadata(item.Key, item.Value.ElementType, dimensions));
        }

        return new ReadOnlyDictionary<string, LayaTensorMetadata>(snapshots);
    }

    /// <summary>驗證鎖定 English export 的必要輸入與輸出 schema。</summary>
    private static void ValidateSchema(
        IReadOnlyDictionary<string, LayaTensorMetadata> inputMetadata,
        IReadOnlyDictionary<string, LayaTensorMetadata> outputMetadata,
        string modelPath)
    {
        ValidateRequiredTensors(inputMetadata, RequiredInputs, "input", modelPath);
        ValidateRequiredTensors(outputMetadata, RequiredOutputs, "output", modelPath);
    }

    /// <summary>驗證必要 tensor 存在且 dtype、rank 相容。</summary>
    private static void ValidateRequiredTensors(
        IReadOnlyDictionary<string, LayaTensorMetadata> actual,
        IReadOnlyDictionary<string, TensorContract> required,
        string direction,
        string modelPath)
    {
        foreach (var item in required)
        {
            if (!actual.TryGetValue(item.Key, out var metadata))
            {
                throw new LayaConfigurationException(
                    $"ONNX {direction} '{item.Key}' is missing from '{modelPath}'.",
                    modelPath);
            }

            if (metadata.ElementType != item.Value.ElementType ||
                metadata.Rank != item.Value.Rank ||
                !HasCompatibleDimensions(metadata, item.Value))
            {
                throw new LayaConfigurationException(
                    $"ONNX {direction} '{item.Key}' has dtype '{metadata.ElementType.Name}' and rank " +
                    $"{metadata.Rank}; expected '{item.Value.ElementType.Name}' and rank {item.Value.Rank}.",
                    modelPath);
            }
        }
    }

    /// <summary>驗證 schema 指定的固定維度；null 維度代表可動態變化。</summary>
    private static bool HasCompatibleDimensions(
        LayaTensorMetadata actual,
        TensorContract expected)
    {
        if (expected.Dimensions is null)
        {
            return true;
        }

        return expected.Dimensions
            .Select((dimension, index) =>
                !dimension.HasValue || actual.Dimensions[index] == dimension.Value)
            .All(isCompatible => isCompatible);
    }

    /// <summary>保存單一 tensor 的最低相容條件。</summary>
    private sealed class TensorContract
    {
        /// <summary>建立 tensor schema 條件。</summary>
        public TensorContract(
            Type elementType,
            int rank,
            IReadOnlyList<long?>? dimensions = null)
        {
            ElementType = elementType;
            Rank = rank;
            Dimensions = dimensions;
        }

        /// <summary>取得預期 CLR 元素型別。</summary>
        public Type ElementType { get; }

        /// <summary>取得預期 tensor rank。</summary>
        public int Rank { get; }

        /// <summary>取得預期固定維度；null 表示不限制該維度。</summary>
        public IReadOnlyList<long?>? Dimensions { get; }
    }
}
