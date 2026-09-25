using System.Diagnostics;
using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.PostProcessing;
using Laya.Core.Tokenization;
using Laya.MemoryProbe.Configuration;
using Laya.MemoryProbe.Telemetry;
using Laya.Shared.Configuration;
using Microsoft.ML.OnnxRuntime;

namespace Laya.MemoryProbe.Scenarios;

/// <summary>為單一 Probe process 建立並重用指定 scenario 的 managed/native resources。</summary>
internal sealed class MemoryProbeScenarioExecution : IDisposable
{
    private static readonly string[] OutputNames = { "logits", "act_probs" };
    private readonly MemoryProbeOptions _options;
    private readonly LayaProfileResolution _profile;
    private readonly MemoryWorkloadCatalog _workloads;
    private readonly object _checksumLock = new();
    private double _checksum;
    private LayaTokenizer? _tokenizer;
    private LayaModelConfig? _modelConfig;
    private LayaSequenceBuilder? _sequenceBuilder;
    private LayaRequest? _request;
    private LayaInputBatch? _batch;
    private readonly List<RunOnlyWorker> _runOnlyWorkers = new();
    private LayaOnnxSession? _session;
    private LayaDecisionEngine? _decisionEngine;
    private LayaInferenceResult? _calibrationResult;
    private LayaPostProcessor? _postProcessor;
    private MemoryTokenizerInput? _tokenizerInput;
    private IReadOnlyList<ScheduledRequest> _scheduledRequests = Array.Empty<ScheduledRequest>();
    private int _disposed;

    /// <summary>建立尚未載入任何 scenario resource 的執行狀態。</summary>
    private MemoryProbeScenarioExecution(
        MemoryProbeOptions options,
        LayaProfileResolution profile,
        MemoryWorkloadCatalog workloads)
    {
        _options = options;
        _profile = profile;
        _workloads = workloads;
    }

    /// <summary>取得目前 scenario 是否持有長生命週期 ONNX inference session。</summary>
    public bool HasInferenceSession => _session is not null || _decisionEngine is not null;

    /// <summary>取得最近一次 operation 的實際 sequence length。</summary>
    public int? LastSequenceLength { get; private set; }

    /// <summary>取得此 run 是否刻意混合 state shape，不可作固定 workload slope。</summary>
    public bool IsStateScheduled => _scheduledRequests.Count > 0;

    /// <summary>取得最近一次 full-pipeline request 的 truncation 狀態。</summary>
    public bool? LastWasTruncated { get; private set; }

    /// <summary>取得最近一次 native Run 的耗時。</summary>
    public TimeSpan? LastInferenceDuration { get; private set; }

    /// <summary>取得固定輸出檢查所累積的 scalar checksum。</summary>
    public double Checksum
    {
        get
        {
            lock (_checksumLock)
            {
                return _checksum;
            }
        }
    }

    /// <summary>依選定 scenario 建立資源；初始化失敗時釋放所有已取得 owner。</summary>
    public static MemoryProbeScenarioExecution Create(
        MemoryProbeOptions options,
        LayaProfileResolution profile,
        MemoryWorkloadCatalog workloads)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(workloads);
        if (options.IsLoadOnly || options.Scenario is null)
        {
            throw new ArgumentException("Load-only mode does not create a request scenario.", nameof(options));
        }

        var execution = new MemoryProbeScenarioExecution(options, profile, workloads);
        try
        {
            execution.Initialize();
            return execution;
        }
        catch
        {
            execution.Dispose();
            throw;
        }
    }

    /// <summary>執行一個 scenario operation 並回傳其計時口徑。</summary>
    public TimeSpan Execute(
        int requestIndex,
        Action<int, string, ProcessMemorySnapshot>? sessionCycleSample = null)
    {
        return ExecuteSample(requestIndex, 0, sessionCycleSample).EndToEndDuration;
    }

    /// <summary>執行單一 request 並回傳 thread-local latency、shape 與 inference metadata。</summary>
    public ProbeOperationSample ExecuteSample(
        int requestIndex,
        Action<int, string, ProcessMemorySnapshot>? sessionCycleSample = null)
    {
        return ExecuteSample(requestIndex, 0, sessionCycleSample);
    }

    /// <summary>使用明確 worker slot 執行 request，支援 run-only 的私有 input owners。</summary>
    public ProbeOperationSample ExecuteSample(
        int requestIndex,
        int workerIndex,
        Action<int, string, ProcessMemorySnapshot>? sessionCycleSample = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        return _options.Scenario switch
        {
            MemoryProbeScenario.TokenizerOnly => ExecuteTokenizerOnly(),
            MemoryProbeScenario.SequenceOnly => ExecuteSequenceOnly(),
            MemoryProbeScenario.TensorOnly => ExecuteTensorOnly(),
            MemoryProbeScenario.RunOnly => ExecuteRunOnly(workerIndex),
            MemoryProbeScenario.CalibrationOnly => ExecuteCalibrationOnly(),
            MemoryProbeScenario.FullPipeline => ExecuteFullPipeline(requestIndex),
            MemoryProbeScenario.SessionRecreate => ExecuteSessionRecreate(requestIndex, sessionCycleSample),
            _ => throw new InvalidOperationException("The selected probe scenario is unsupported.")
        };
    }

    /// <summary>依照 scenario 載入必要資源，禁止 component-only 建立 ONNX session。</summary>
    private void Initialize()
    {
        switch (_options.Scenario)
        {
            case MemoryProbeScenario.TokenizerOnly:
                _tokenizer = LoadTokenizer();
                _tokenizerInput = _workloads.GetTokenizerInput(_options.WorkloadId);
                break;
            case MemoryProbeScenario.SequenceOnly:
                LoadTokenizerAndConfig();
                _request = _workloads.CreateRequest(_options.StateProfile, _options.QuestionCount);
                _sequenceBuilder = new LayaSequenceBuilder(_tokenizer!, _modelConfig!);
                break;
            case MemoryProbeScenario.TensorOnly:
                LoadTokenizerAndConfig();
                BuildTransactionBatch();
                break;
            case MemoryProbeScenario.RunOnly:
                _session = OpenSession();
                _tokenizer = LayaTokenizer.Load(
                    _session.Bundle.TokenizerPath,
                    _session.Bundle.TokenizerConfigPath);
                _modelConfig = _session.Bundle.Config;
                BuildTransactionBatch();
                for (var workerIndex = 0; workerIndex < _options.Concurrency; workerIndex++)
                {
                    _runOnlyWorkers.Add(RunOnlyWorker.Create(_batch!));
                }
                break;
            case MemoryProbeScenario.CalibrationOnly:
                LoadTokenizerAndConfig();
                _request = _workloads.CreateCalibrationRequest();
                _sequenceBuilder = new LayaSequenceBuilder(_tokenizer!, _modelConfig!);
                _batch = _sequenceBuilder.Build(_request);
                _calibrationResult = _workloads.CreateCalibrationResult();
                _postProcessor = new LayaPostProcessor();
                LastSequenceLength = _batch.SequenceLength;
                break;
            case MemoryProbeScenario.FullPipeline:
                _decisionEngine = LayaDecisionEngine.Open(CreateLayaOptions());
                InitializeFullPipelineRequests();
                break;
            case MemoryProbeScenario.SessionRecreate:
                LoadTokenizerAndConfig();
                BuildTransactionBatch();
                break;
            default:
                throw new InvalidOperationException("The selected probe scenario is unsupported.");
        }
    }

    /// <summary>只編碼固定 tokenizer 文字並消費 token checksum。</summary>
    private ProbeOperationSample ExecuteTokenizerOnly()
    {
        var stopwatch = Stopwatch.StartNew();
        var tokenIds = _tokenizer!.Encode(_tokenizerInput!.Text);
        LastSequenceLength = tokenIds.Count;
        AddChecksum(tokenIds.Count == 0 ? 0 : tokenIds[0]);
        stopwatch.Stop();
        return new ProbeOperationSample(stopwatch.Elapsed, null, false, tokenIds.Count, false);
    }

    /// <summary>使用真實 sequence builder 建立一次 batch，不呼叫 ONNX。</summary>
    private ProbeOperationSample ExecuteSequenceOnly()
    {
        var stopwatch = Stopwatch.StartNew();
        var batch = _sequenceBuilder!.Build(_request!);
        LastSequenceLength = batch.SequenceLength;
        AddChecksum(batch.InputIds.Length);
        stopwatch.Stop();
        return new ProbeOperationSample(stopwatch.Elapsed, null, false, batch.SequenceLength, false);
    }

    /// <summary>依預建 batch 建立並釋放五項 input OrtValues，不執行 Run。</summary>
    private ProbeOperationSample ExecuteTensorOnly()
    {
        var stopwatch = Stopwatch.StartNew();
        using var owner = LayaInputTensorOwner.Create(_batch!);
        AddChecksum(owner.Inputs.Count);
        stopwatch.Stop();
        return new ProbeOperationSample(stopwatch.Elapsed, null, false, _batch!.SequenceLength, false);
    }

    /// <summary>重用固定 inputs、RunOptions 與 session 執行一次 native ONNX Run。</summary>
    private ProbeOperationSample ExecuteRunOnly(int workerIndex)
    {
        if ((uint)workerIndex >= (uint)_runOnlyWorkers.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(workerIndex));
        }

        var worker = _runOnlyWorkers[workerIndex];
        var stopwatch = Stopwatch.StartNew();
        using var outputs = _session!.Run(worker.RunOptions, worker.InputOwner.Inputs, OutputNames);
        stopwatch.Stop();

        var values = outputs.ToArray();
        if (values.Length != OutputNames.Length)
        {
            throw new InvalidOperationException("ONNX Runtime returned an unexpected output count in run-only scenario.");
        }

        var logits = values[0].GetTensorDataAsSpan<float>();
        if (logits.Length > 0)
        {
            AddChecksum(logits[0]);
        }

        LastInferenceDuration = stopwatch.Elapsed;
        LastSequenceLength = _batch!.SequenceLength;
        return new ProbeOperationSample(
            stopwatch.Elapsed,
            stopwatch.Elapsed,
            false,
            _batch!.SequenceLength,
            false);
    }

    /// <summary>重用長生命週期 decision engine 執行一個完整 end-to-end request。</summary>
    private ProbeOperationSample ExecuteFullPipeline(int requestIndex)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = _decisionEngine!.Decide(GetRequestForIndex(requestIndex));
        stopwatch.Stop();
        LastInferenceDuration = result.InferenceDuration;
        LastSequenceLength = result.SequenceLength;
        LastWasTruncated = result.WasTruncated;
        AddChecksum(result.Answers.Sum(answer => answer.Probability));
        return new ProbeOperationSample(
            stopwatch.Elapsed,
            result.InferenceDuration,
            false,
            result.SequenceLength,
            result.WasTruncated);
    }

    /// <summary>逐次建立 session、執行單次固定 batch 並計入完整 dispose 時間。</summary>
    private ProbeOperationSample ExecuteSessionRecreate(
        int requestIndex,
        Action<int, string, ProcessMemorySnapshot>? sessionCycleSample)
    {
        sessionCycleSample?.Invoke(
            requestIndex,
            "before-session-create",
            ProcessMemoryCollector.Capture());
        var stopwatch = Stopwatch.StartNew();
        LayaInferenceResult result;
        using (var session = OpenSession())
        {
            result = new LayaInferenceRunner(session).Run(_batch!);
        }

        stopwatch.Stop();
        LastInferenceDuration = result.RunDuration;
        LastSequenceLength = _batch!.SequenceLength;
        if (result.Logits.Length > 0)
        {
            AddChecksum(result.Logits[0]);
        }

        sessionCycleSample?.Invoke(
            requestIndex,
            "after-session-dispose",
            ProcessMemoryCollector.Capture());
        return new ProbeOperationSample(
            stopwatch.Elapsed,
            result.RunDuration,
            false,
            _batch!.SequenceLength,
            false);
    }

    /// <summary>對固定 batch 與 raw outputs 執行 calibration，不呼叫 ONNX。</summary>
    private ProbeOperationSample ExecuteCalibrationOnly()
    {
        var stopwatch = Stopwatch.StartNew();
        var answers = _postProcessor!.Process(
            _request!,
            _batch!,
            _calibrationResult!,
            _modelConfig!);
        AddChecksum(answers.Sum(answer => answer.Probability));
        stopwatch.Stop();
        return new ProbeOperationSample(stopwatch.Elapsed, null, false, _batch!.SequenceLength, false);
    }

    /// <summary>載入 tokenizer 與 config，但不建立 inference session。</summary>
    private void LoadTokenizerAndConfig()
    {
        _tokenizer = LoadTokenizer();
        _modelConfig = LayaModelConfig.Load(Path.Combine(_profile.ModelRoot, "laya_config.json"));
    }

    /// <summary>依 verified bundle root 載入 tokenizer 檔案。</summary>
    private LayaTokenizer LoadTokenizer()
    {
        var tokenizerRoot = Path.Combine(_profile.ModelRoot, "tokenizer");
        return LayaTokenizer.Load(
            Path.Combine(tokenizerRoot, "tokenizer.json"),
            Path.Combine(tokenizerRoot, "tokenizer_config.json"));
    }

    /// <summary>使用 resolved multilingual model identity 與 arena 組態開啟 session。</summary>
    private LayaOnnxSession OpenSession()
    {
        return LayaOnnxSession.Open(CreateLayaOptions());
    }

    /// <summary>建立 host 已解析的 Core options，不讓 Core 推測 model path。</summary>
    private LayaOptions CreateLayaOptions()
    {
        return new LayaOptions(
            _profile.ModelRoot,
            _profile.Name,
            _profile.CheckpointRevision,
            enableCpuMemArena: _options.CpuArenaEnabled);
    }

    /// <summary>建立交易 request 與固定 tensor batch。</summary>
    private void BuildTransactionBatch()
    {
        _request = _workloads.CreateRequest(_options.StateProfile, _options.QuestionCount);
        _sequenceBuilder = new LayaSequenceBuilder(_tokenizer!, _modelConfig!);
        _batch = _sequenceBuilder.Build(_request);
        LastSequenceLength = _batch.SequenceLength;
    }

    /// <summary>預先建立固定 workload 或各 state schedule segment 的 request fixture。</summary>
    private void InitializeFullPipelineRequests()
    {
        if (_options.StateSchedule.Count == 0)
        {
            _request = _workloads.CreateRequest(_options.StateProfile, _options.QuestionCount);
            return;
        }

        var schedule = new List<ScheduledRequest>(_options.StateSchedule.Count);
        var endRequestIndex = 0;
        foreach (var segment in _options.StateSchedule)
        {
            endRequestIndex = checked(endRequestIndex + segment.Requests);
            schedule.Add(new ScheduledRequest(
                endRequestIndex,
                _workloads.CreateRequest(segment.StateProfile, _options.QuestionCount)));
        }

        _scheduledRequests = schedule;
    }

    /// <summary>取得固定 run 或指定 request index 對應的 state schedule request。</summary>
    private LayaRequest GetRequestForIndex(int requestIndex)
    {
        if (_scheduledRequests.Count == 0)
        {
            return _request!;
        }

        if (requestIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestIndex));
        }

        foreach (var scheduledRequest in _scheduledRequests)
        {
            if (requestIndex < scheduledRequest.EndRequestIndex)
            {
                return scheduledRequest.Request;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(requestIndex),
            "Request index is outside the configured state schedule.");
    }

    /// <summary>釋放本 execution 擁有的 native／managed owners，持續嘗試清理所有資源。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var exceptions = new List<Exception>();
        foreach (var worker in _runOnlyWorkers)
        {
            DisposeResource(worker, exceptions);
        }

        _runOnlyWorkers.Clear();
        DisposeResource(Interlocked.Exchange(ref _decisionEngine, null), exceptions);
        DisposeResource(Interlocked.Exchange(ref _session, null), exceptions);
        DisposeResource(Interlocked.Exchange(ref _tokenizer, null), exceptions);

        if (exceptions.Count == 1)
        {
            throw exceptions[0];
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException("One or more memory probe resources could not be disposed.", exceptions);
        }
    }

    /// <summary>釋放單一資源並保存錯誤以便後續 owners 仍可釋放。</summary>
    private static void DisposeResource(IDisposable? resource, ICollection<Exception> exceptions)
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            resource.Dispose();
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
    }

    /// <summary>以 lock 保護共享 scenario checksum，避免並行 worker 遺失更新。</summary>
    private void AddChecksum(double value)
    {
        lock (_checksumLock)
        {
            _checksum += value;
        }
    }

    /// <summary>保存 run-only 專屬 inputs 與 RunOptions，避免 worker 共用 mutable native wrappers。</summary>
    private sealed class RunOnlyWorker : IDisposable
    {
        private LayaInputTensorOwner? _inputOwner;
        private RunOptions? _runOptions;

        /// <summary>建立 worker 私有的 five-input OrtValue owner 與 RunOptions。</summary>
        private RunOnlyWorker(LayaInputTensorOwner inputOwner, RunOptions runOptions)
        {
            _inputOwner = inputOwner;
            _runOptions = runOptions;
        }

        /// <summary>取得 worker 固定 input tensors。</summary>
        public LayaInputTensorOwner InputOwner =>
            Volatile.Read(ref _inputOwner) ?? throw new ObjectDisposedException(nameof(RunOnlyWorker));

        /// <summary>取得 worker 私有 native RunOptions。</summary>
        public RunOptions RunOptions =>
            Volatile.Read(ref _runOptions) ?? throw new ObjectDisposedException(nameof(RunOnlyWorker));

        /// <summary>建立 worker resources；建構失敗時釋放已建立的 inputs。</summary>
        public static RunOnlyWorker Create(LayaInputBatch batch)
        {
            var inputOwner = LayaInputTensorOwner.Create(batch);
            try
            {
                return new RunOnlyWorker(inputOwner, new RunOptions());
            }
            catch
            {
                inputOwner.Dispose();
                throw;
            }
        }

        /// <summary>釋放 worker RunOptions 與 input owner；重複呼叫安全。</summary>
        public void Dispose()
        {
            var runOptions = Interlocked.Exchange(ref _runOptions, null);
            var inputOwner = Interlocked.Exchange(ref _inputOwner, null);
            List<Exception>? exceptions = null;
            DisposeOne(runOptions, ref exceptions);
            DisposeOne(inputOwner, ref exceptions);
            if (exceptions is { Count: 1 })
            {
                throw exceptions[0];
            }

            if (exceptions is { Count: > 1 })
            {
                throw new AggregateException("Run-only worker resources could not all be disposed.", exceptions);
            }
        }

        /// <summary>嘗試釋放一個 resource 並累積 Dispose 例外。</summary>
        private static void DisposeOne(IDisposable? resource, ref List<Exception>? exceptions)
        {
            if (resource is null)
            {
                return;
            }

            try
            {
                resource.Dispose();
            }
            catch (Exception exception)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(exception);
            }
        }
    }

    /// <summary>保存 schedule segment 的 exclusive end index 與預建 request。</summary>
    private sealed class ScheduledRequest
    {
        /// <summary>建立固定 request segment。</summary>
        public ScheduledRequest(int endRequestIndex, LayaRequest request)
        {
            EndRequestIndex = endRequestIndex;
            Request = request;
        }

        /// <summary>取得此 segment 不包含的結束 request index。</summary>
        public int EndRequestIndex { get; }

        /// <summary>取得此 segment 預先建立的 Laya request。</summary>
        public LayaRequest Request { get; }
    }
}
