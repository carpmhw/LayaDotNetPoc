using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Evaluation;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.PostProcessing;
using Laya.Core.Serialization;
using Laya.Core.Tokenization;
using Laya.Shared.Configuration;

var profile = ResolveProfile(args);
var modelRoot = profile.ModelRoot;
var outputPath = GetOption(args, "--output");
var benchmarkArgs = RemoveOptions(
    args,
    "--model-root",
    "--profile",
    "--config",
    "--output",
    "--concurrency",
    "--cold-start",
    "--metadata",
    "--collect",
    "--practical");
Environment.SetEnvironmentVariable("LAYA_MODEL_ROOT", modelRoot);
Environment.SetEnvironmentVariable("LAYA_PROFILE", profile.Name);
Environment.SetEnvironmentVariable("LAYA_CHECKPOINT_REVISION", profile.CheckpointRevision);

if (HasFlag(args, "--cold-start"))
{
    WriteOutput(RunColdStart(profile), outputPath);
    return;
}

if (HasFlag(args, "--metadata"))
{
    WriteOutput(PrintMetadata(profile), outputPath);
    return;
}

if (HasFlag(args, "--collect"))
{
    WriteOutput(RunCollection(profile), outputPath);
    return;
}

if (HasFlag(args, "--practical"))
{
    WriteOutput(RunPracticalCollection(profile), outputPath);
    return;
}

if (HasFlag(args, "--concurrency"))
{
    var concurrency = ParseRequiredIntOption(args, "--concurrency");
    WriteOutput(RunConcurrency(profile, concurrency), outputPath);
    return;
}

var process = Process.GetCurrentProcess();
var workingSetBefore = process.WorkingSet64;
BenchmarkRunner.Run<LayaBenchmarks>(
    DefaultConfig.Instance.AddJob(Job.Default
        .WithLaunchCount(1)
        .WithWarmupCount(5)
        .WithIterationCount(100)),
    benchmarkArgs);
process.Refresh();
Console.WriteLine($"Process working set delta bytes: {process.WorkingSet64 - workingSetBefore}");

/// <summary>取得需要值的 benchmark command-line option。</summary>
static string? GetOption(IReadOnlyList<string> args, string name)
{
    for (var index = 0; index < args.Count - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    return null;
}

/// <summary>解析 benchmark 使用的 profile 設定與相對路徑。</summary>
static LayaProfileResolution ResolveProfile(IReadOnlyList<string> args)
{
    var configPath = GetOption(args, "--config");
    var settings = configPath is null
        ? LayaProfileResolver.LoadEnvironment()
        : LayaProfileResolver.LoadJson(configPath);
    return LayaProfileResolver.Resolve(
        GetOption(args, "--model-root"),
        GetOption(args, "--profile"),
        settings,
        Directory.GetCurrentDirectory());
}

/// <summary>判斷 command-line flag 是否存在。</summary>
static bool HasFlag(IReadOnlyList<string> args, string name)
{
    return args.Any(argument => string.Equals(argument, name, StringComparison.Ordinal));
}

/// <summary>移除 application-specific options，避免 BenchmarkDotNet 將其視為未知參數。</summary>
static string[] RemoveOptions(IReadOnlyList<string> args, params string[] names)
{
    var nameSet = new HashSet<string>(names, StringComparer.Ordinal);
    var valueOptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "--model-root",
        "--profile",
        "--config",
        "--output",
        "--concurrency"
    };
    var filtered = new List<string>(args.Count);
    for (var index = 0; index < args.Count; index++)
    {
        if (nameSet.Contains(args[index]))
        {
            if (valueOptions.Contains(args[index]))
            {
                index++;
            }

            continue;
        }

        filtered.Add(args[index]);
    }

    return filtered.ToArray();
}

/// <summary>解析必須的整數 command-line option。</summary>
static int ParseRequiredIntOption(IReadOnlyList<string> args, string name)
{
    var value = GetOption(args, name);
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
    {
        throw new ArgumentException($"Option '{name}' requires an integer value.", name);
    }

    return parsed;
}

/// <summary>將 benchmark artifact 寫到 stdout 或指定的 JSON 路徑。</summary>
static void WriteOutput(object output, string? outputPath)
{
    var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        Console.WriteLine(json);
        return;
    }

    var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllText(outputPath, json);
    Console.WriteLine($"Benchmark artifact: {outputPath}");
}

/// <summary>在獨立程序中量測 model load、首次 Run 與載入前後記憶體。</summary>
static object RunColdStart(LayaProfileResolution profile)
{
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var workingSetBefore = process.WorkingSet64;
    var managedBefore = GC.GetTotalMemory(forceFullCollection: true);

    var loadStopwatch = Stopwatch.StartNew();
    using var engine = LayaDecisionEngine.Open(new LayaOptions(
        profile.ModelRoot,
        profile.Name,
        profile.CheckpointRevision));
    loadStopwatch.Stop();
    process.Refresh();
    var workingSetAfterLoad = process.WorkingSet64;
    var managedAfterLoad = GC.GetTotalMemory(forceFullCollection: true);

    var firstRunStopwatch = Stopwatch.StartNew();
    var result = engine.Decide(BenchmarkScenarioFactory.CreateRequest(1, "short"));
    firstRunStopwatch.Stop();
    process.Refresh();
    var workingSetAfterFirstRun = process.WorkingSet64;
    var managedAfterFirstRun = GC.GetTotalMemory(forceFullCollection: true);

    var output = new
    {
        schemaVersion = 1,
        mode = "cold-start",
        processId = Environment.ProcessId,
        runId = CreateRunId(),
        profile = profile.Name,
        modelRoot = profile.ModelRoot,
        checkpointRevision = profile.CheckpointRevision,
        environment = CreateEnvironmentSnapshot(),
        loadMs = Math.Round(loadStopwatch.Elapsed.TotalMilliseconds, 3),
        firstRunEndToEndMs = Math.Round(firstRunStopwatch.Elapsed.TotalMilliseconds, 3),
        runOnlyMs = Math.Round(result.InferenceDuration.TotalMilliseconds, 3),
        workingSetBeforeBytes = workingSetBefore,
        workingSetAfterLoadBytes = workingSetAfterLoad,
        workingSetAfterFirstRunBytes = workingSetAfterFirstRun,
        peakWorkingSetBytes = Math.Max(workingSetAfterLoad, workingSetAfterFirstRun),
        managedBeforeBytes = managedBefore,
        managedAfterLoadBytes = managedAfterLoad,
        managedAfterFirstRunBytes = managedAfterFirstRun
    };

    return output;
}

/// <summary>輸出九組 benchmark 的實際 token 長度與截斷狀態。</summary>
static object PrintMetadata(LayaProfileResolution profile)
{
    var bundle = LayaModelValidator.ValidateBundle(
        profile.ModelRoot,
        manifestPath: null,
        options: new LayaOptions(profile.ModelRoot, profile.Name, profile.CheckpointRevision));
    using var tokenizer = LayaTokenizer.Load(bundle.TokenizerPath, bundle.TokenizerConfigPath);
    var builder = new LayaSequenceBuilder(tokenizer, bundle.Config);
    var rows = new List<ScenarioMetadata>();

    foreach (var questionCount in new[] { 1, 2, 5 })
    {
        foreach (var stateProfile in BenchmarkScenarioFactory.StateProfiles)
        {
            var request = BenchmarkScenarioFactory.CreateRequest(questionCount, stateProfile);
            var batch = builder.Build(request);
            var stateTokenCount = tokenizer.Encode(LayaStateSerializer.Serialize(request.State)).Count;
            rows.Add(new ScenarioMetadata(
                questionCount,
                stateProfile,
                stateTokenCount,
                batch.SequenceLength,
                batch.MarkerCount,
                batch.Questions.Any(question => question.SequenceLength == bundle.Config.MaxLength)));
        }
    }

    var output = new
    {
        schemaVersion = 1,
        runId = CreateRunId(),
        profile = profile.Name,
        modelRoot = profile.ModelRoot,
        checkpointRevision = profile.CheckpointRevision,
        environment = CreateEnvironmentSnapshot(),
        scenarios = rows
    };
    return output;
}

/// <summary>以五次 warmup 與一百次觀測收集九組 Run-only／end-to-end 統計。</summary>
static object RunCollection(LayaProfileResolution profile)
{
    var options = new LayaOptions(profile.ModelRoot, profile.Name, profile.CheckpointRevision);
    var bundle = LayaModelValidator.ValidateBundle(profile.ModelRoot, manifestPath: null, options);
    using var session = LayaOnnxSession.Open(options);
    using var tokenizer = LayaTokenizer.Load(bundle.TokenizerPath, bundle.TokenizerConfigPath);
    var builder = new LayaSequenceBuilder(tokenizer, bundle.Config);
    var runner = new LayaInferenceRunner(session);
    var postProcessor = new LayaPostProcessor();
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var workingSetBefore = process.WorkingSet64;
    var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
    var peakWorkingSet = workingSetBefore;
    var results = new List<ScenarioMeasurement>();
    var memorySamples = new List<MemorySample>();

    foreach (var questionCount in new[] { 1, 2, 5 })
    {
        foreach (var stateProfile in BenchmarkScenarioFactory.StateProfiles)
        {
            var request = BenchmarkScenarioFactory.CreateRequest(questionCount, stateProfile);
            var batch = builder.Build(request);
            var runOnly = new List<double>();
            var endToEnd = new List<double>();

            for (var iteration = 0; iteration < 105; iteration++)
            {
                var stopwatch = Stopwatch.StartNew();
                var currentBatch = builder.Build(request);
                var raw = runner.Run(currentBatch);
                _ = postProcessor.Process(request, currentBatch, raw, bundle.Config);
                stopwatch.Stop();
                process.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                memorySamples.Add(new MemorySample(
                    iteration,
                    process.WorkingSet64,
                    GC.GetTotalMemory(forceFullCollection: false)));

                if (iteration >= 5)
                {
                    runOnly.Add(raw.RunDuration.TotalMilliseconds);
                    endToEnd.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            results.Add(new ScenarioMeasurement(
                questionCount,
                stateProfile,
                tokenizer.Encode(LayaStateSerializer.Serialize(request.State)).Count,
                batch.SequenceLength,
                batch.MarkerCount,
                batch.Questions.Any(question => question.SequenceLength == bundle.Config.MaxLength),
                5,
                runOnly.Count,
                LayaBenchmarkStatistics.Summarize(runOnly, 5),
                LayaBenchmarkStatistics.Summarize(endToEnd, 5),
                runOnly,
                endToEnd));
        }
    }

    process.Refresh();
    var output = new
    {
        schemaVersion = 1,
        mode = "warm-collection",
        runId = CreateRunId(),
        profile = profile.Name,
        modelRoot = profile.ModelRoot,
        checkpointRevision = profile.CheckpointRevision,
        environment = CreateEnvironmentSnapshot(),
        warmupCount = 5,
        sampleCount = 100,
        processWorkingSetBeforeBytes = workingSetBefore,
        processWorkingSetAfterBytes = process.WorkingSet64,
        peakWorkingSetBytes = peakWorkingSet,
        managedBeforeBytes = managedBefore,
        managedAfterBytes = GC.GetTotalMemory(forceFullCollection: true),
        percentileAlgorithm = "linear interpolation on sorted samples at (n-1)*p",
        memorySamples,
        scenarios = results
    };

    return output;
}

/// <summary>以四欄 transaction state 暖機後量測 200 次實務 Choice＋Noul request。</summary>
static object RunPracticalCollection(LayaProfileResolution profile)
{
    const int warmupCount = 5;
    const int sampleCount = 200;
    var options = new LayaOptions(profile.ModelRoot, profile.Name, profile.CheckpointRevision);
    var bundle = LayaModelValidator.ValidateBundle(profile.ModelRoot, manifestPath: null, options);
    using var session = LayaOnnxSession.Open(options);
    using var tokenizer = LayaTokenizer.Load(bundle.TokenizerPath, bundle.TokenizerConfigPath);
    var builder = new LayaSequenceBuilder(tokenizer, bundle.Config);
    var runner = new LayaInferenceRunner(session);
    var postProcessor = new LayaPostProcessor();
    var request = BenchmarkScenarioFactory.CreateTransactionRequest(0);
    var batch = builder.Build(request);
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var workingSetBefore = process.WorkingSet64;
    var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
    var peakWorkingSet = workingSetBefore;
    var memorySamples = new List<MemorySample>();
    var runOnly = new List<double>(sampleCount);
    var endToEnd = new List<double>(sampleCount);

    for (var iteration = 0; iteration < warmupCount + sampleCount; iteration++)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentBatch = builder.Build(request);
        var raw = runner.Run(currentBatch);
        _ = postProcessor.Process(request, currentBatch, raw, bundle.Config);
        stopwatch.Stop();
        process.Refresh();
        peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
        memorySamples.Add(new MemorySample(
            iteration,
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false)));

        if (iteration >= warmupCount)
        {
            runOnly.Add(raw.RunDuration.TotalMilliseconds);
            endToEnd.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    process.Refresh();
    return new
    {
        schemaVersion = 1,
        mode = "practical-collection",
        runId = CreateRunId(),
        profile = profile.Name,
        modelRoot = profile.ModelRoot,
        checkpointRevision = profile.CheckpointRevision,
        environment = CreateEnvironmentSnapshot(),
        warmupCount,
        sampleCount,
        processWorkingSetBeforeBytes = workingSetBefore,
        processWorkingSetAfterBytes = process.WorkingSet64,
        peakWorkingSetBytes = peakWorkingSet,
        managedBeforeBytes = managedBefore,
        managedAfterBytes = GC.GetTotalMemory(forceFullCollection: true),
        percentileAlgorithm = "linear interpolation on sorted samples at (n-1)*p",
        memorySamples,
        scenario = new
        {
            questionCount = request.Questions.Count,
            stateTokenCount = tokenizer.Encode(LayaStateSerializer.Serialize(request.State)).Count,
            sequenceLength = batch.SequenceLength,
            markerCount = batch.MarkerCount,
            wasTruncated = batch.Questions.Any(question => question.SequenceLength == bundle.Config.MaxLength),
            runOnly = LayaBenchmarkStatistics.Summarize(runOnly, warmupCount),
            endToEnd = LayaBenchmarkStatistics.Summarize(endToEnd, warmupCount),
            runOnlySamples = runOnly,
            endToEndSamples = endToEnd
        }
    };
}

/// <summary>以共享 engine 執行指定 concurrency 的 50 次 request 並驗證結果保序。</summary>
static object RunConcurrency(LayaProfileResolution profile, int concurrency)
{
    if (concurrency is not (1 or 2 or 4))
    {
        throw new ArgumentOutOfRangeException(nameof(concurrency), "Concurrency must be 1, 2 or 4.");
    }

    const int warmupCount = 5;
    const int sampleCount = 50;
    var options = new LayaOptions(profile.ModelRoot, profile.Name, profile.CheckpointRevision);
    using var engine = LayaDecisionEngine.Open(options);
    var requests = Enumerable.Range(0, sampleCount)
        .Select(index => new IndexedTransaction(index, BenchmarkScenarioFactory.CreateTransactionRequest(index)))
        .ToArray();

    for (var index = 0; index < warmupCount; index++)
    {
        _ = engine.Decide(BenchmarkScenarioFactory.CreateTransactionRequest(-index - 1));
    }

    var expectedAnswers = requests.ToDictionary(
        item => item.Index,
        item => engine.Decide(item.Request).Answers
            .Select(answer => answer.SelectedOption ?? string.Empty)
            .ToArray());
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var workingSetBefore = process.WorkingSet64;
    var memorySamples = new ConcurrentBag<MemorySample>();
    var measurements = new ConcurrentBag<ConcurrencyMeasurement>();
    var memoryLock = new object();
    var peakWorkingSet = workingSetBefore;
    var totalStopwatch = Stopwatch.StartNew();

    Parallel.ForEachAsync(
            requests,
            new ParallelOptions { MaxDegreeOfParallelism = concurrency },
            (item, _) =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var result = engine.Decide(item.Request);
                    var selectedAnswers = result.Answers
                        .Select(answer => answer.SelectedOption ?? string.Empty)
                        .ToArray();
                    stopwatch.Stop();
                    measurements.Add(new ConcurrencyMeasurement(
                        item.Index,
                        true,
                        stopwatch.Elapsed.TotalMilliseconds,
                        selectedAnswers,
                        null,
                        expectedAnswers[item.Index].SequenceEqual(selectedAnswers)));
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    measurements.Add(new ConcurrencyMeasurement(
                        item.Index,
                        false,
                        stopwatch.Elapsed.TotalMilliseconds,
                        Array.Empty<string>(),
                        exception.GetType().Name,
                        false));
                }

                lock (memoryLock)
                {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                    memorySamples.Add(new MemorySample(
                        item.Index,
                        process.WorkingSet64,
                        GC.GetTotalMemory(forceFullCollection: false)));
                }

                return ValueTask.CompletedTask;
            })
        .GetAwaiter()
        .GetResult();
    totalStopwatch.Stop();
    process.Refresh();

    var orderedMeasurements = measurements.OrderBy(measurement => measurement.Index).ToArray();
    var latencies = orderedMeasurements
        .Where(measurement => measurement.Succeeded)
        .Select(measurement => measurement.EndToEndMs)
        .ToArray();
    return new
    {
        schemaVersion = 1,
        mode = "concurrency",
        runId = CreateRunId(),
        profile = profile.Name,
        modelRoot = profile.ModelRoot,
        checkpointRevision = profile.CheckpointRevision,
        environment = CreateEnvironmentSnapshot(),
        concurrency,
        warmupCount,
        sampleCount,
        completedCount = orderedMeasurements.Count(measurement => measurement.Succeeded),
        errorCount = orderedMeasurements.Count(measurement => !measurement.Succeeded),
        integrityFailureCount = orderedMeasurements.Count(measurement => !measurement.IntegrityMatch),
        totalElapsedMs = totalStopwatch.Elapsed.TotalMilliseconds,
        throughputRequestsPerSecond = totalStopwatch.Elapsed.TotalSeconds <= 0
            ? 0
            : orderedMeasurements.Count(measurement => measurement.Succeeded) / totalStopwatch.Elapsed.TotalSeconds,
        processWorkingSetBeforeBytes = workingSetBefore,
        processWorkingSetAfterBytes = process.WorkingSet64,
        peakWorkingSetBytes = peakWorkingSet,
        latency = SummarizeOrNull(latencies, warmupCount),
        memorySamples = memorySamples.OrderBy(sample => sample.Iteration).ToArray(),
        measurements = orderedMeasurements,
        errors = orderedMeasurements
            .Where(measurement => !measurement.Succeeded)
            .Select(measurement => new { measurement.Index, error = measurement.ErrorType })
            .ToArray()
    };
}

/// <summary>在沒有成功樣本時回傳 null，避免產生虛構 latency 指標。</summary>
static LayaBenchmarkDistributionSummary? SummarizeOrNull(
    IReadOnlyList<double> values,
    int warmupCount)
{
    return values.Count == 0
        ? null
        : LayaBenchmarkStatistics.Summarize(values, warmupCount);
}

/// <summary>建立 benchmark run 的唯一識別碼。</summary>
static string CreateRunId()
{
    return $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
}

/// <summary>保存 benchmark 執行環境，避免跨機器比較時遺失口徑。</summary>
static object CreateEnvironmentSnapshot()
{
    return new
    {
        os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        framework = RuntimeInformation.FrameworkDescription,
        processorCount = Environment.ProcessorCount,
        ortThreads = Environment.GetEnvironmentVariable("ORT_NUM_THREADS") ?? "default"
    };
}

/// <summary>描述一組 request 的實際 token 與截斷 metadata。</summary>
file sealed record ScenarioMetadata(
    int QuestionCount,
    string StateProfile,
    int StateTokenCount,
    int SequenceLength,
    int MarkerCount,
    bool WasTruncated);

/// <summary>保存一組 warm benchmark 的 metadata 與兩種 latency 統計。</summary>
file sealed record ScenarioMeasurement(
    int QuestionCount,
    string StateProfile,
    int StateTokenCount,
    int SequenceLength,
    int MarkerCount,
    bool WasTruncated,
    int WarmupCount,
    int SampleCount,
    LayaBenchmarkDistributionSummary RunOnly,
    LayaBenchmarkDistributionSummary EndToEnd,
    IReadOnlyList<double> RunOnlySamples,
    IReadOnlyList<double> EndToEndSamples);

/// <summary>保存每次 measured iteration 的 native process 與 managed memory snapshot。</summary>
file sealed record MemorySample(
    int Iteration,
    long WorkingSetBytes,
    long ManagedBytes);

/// <summary>保存一個並行 benchmark request 的穩定索引與輸入。</summary>
file sealed record IndexedTransaction(
    int Index,
    LayaRequest Request);

/// <summary>保存一個並行 request 的 latency、錯誤與保序驗證結果。</summary>
file sealed record ConcurrencyMeasurement(
    int Index,
    bool Succeeded,
    double EndToEndMs,
    IReadOnlyList<string> SelectedAnswers,
    string? ErrorType,
    bool IntegrityMatch);

/// <summary>BenchmarkDotNet warm matrix，涵蓋九組 question/state 組合。</summary>
[MemoryDiagnoser]
[JsonExporterAttribute.Full]
public class LayaBenchmarks
{
    /// <summary>保存 parent process 傳入的 model root。</summary>
    public static string ModelRootOverride { get; set; } = Path.Combine("models", "laya");

    /// <summary>Benchmark question 數量參數。</summary>
    [Params(1, 2, 5)]
    public int QuestionCount { get; set; }

    /// <summary>Benchmark state 長度參數。</summary>
    [Params("short", "medium", "long")]
    public string StateProfile { get; set; } = "short";

    private LayaOnnxSession? _session;
    private LayaTokenizer? _tokenizer;
    private LayaSequenceBuilder? _builder;
    private LayaInferenceRunner? _runner;
    private LayaPostProcessor? _postProcessor;
    private LayaModelConfig? _config;
    private LayaRequest? _request;
    private LayaInputBatch? _batch;

    /// <summary>載入一次 session、tokenizer 並建立本 scenario 的 request metadata。</summary>
    [GlobalSetup]
    public void Setup()
    {
        var modelRoot = Path.GetFullPath(
            Environment.GetEnvironmentVariable("LAYA_MODEL_ROOT") ?? ModelRootOverride);
        _session = LayaOnnxSession.Open(new LayaOptions(
            modelRoot,
            Environment.GetEnvironmentVariable("LAYA_PROFILE"),
            Environment.GetEnvironmentVariable("LAYA_CHECKPOINT_REVISION")));
        _tokenizer = LayaTokenizer.Load(
            _session.Bundle.TokenizerPath,
            _session.Bundle.TokenizerConfigPath);
        _config = _session.Bundle.Config;
        _builder = new LayaSequenceBuilder(_tokenizer, _config);
        _runner = new LayaInferenceRunner(_session);
        _postProcessor = new LayaPostProcessor();
        _request = BenchmarkScenarioFactory.CreateRequest(QuestionCount, StateProfile);
        _batch = _builder.Build(_request);
    }

    /// <summary>釋放 scenario 的 tokenizer 與長生命週期 CPU session。</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _tokenizer?.Dispose();
        _session?.Dispose();
    }

    /// <summary>量測已載入 session 的 input tensor 建立與單次 ONNX Run pipeline。</summary>
    [Benchmark]
    public LayaInferenceResult WarmRunOnly()
    {
        return GetRunner().Run(_batch ?? throw new InvalidOperationException("Benchmark batch is unavailable."));
    }

    /// <summary>量測不含 model load 的 sequence、Run、calibration 與結果映射 end-to-end latency。</summary>
    [Benchmark]
    public LayaResult WarmEndToEnd()
    {
        var request = _request ?? throw new InvalidOperationException("Benchmark request is unavailable.");
        var batch = GetBuilder().Build(request);
        var raw = GetRunner().Run(batch);
        var answers = GetPostProcessor().Process(request, batch, raw, _config!);
        return new LayaResult(answers, raw.RunDuration);
    }

    /// <summary>取得已由 GlobalSetup 建立的 inference runner。</summary>
    private LayaInferenceRunner GetRunner()
    {
        return _runner ?? throw new InvalidOperationException("Benchmark setup did not initialize the runner.");
    }

    /// <summary>取得已由 GlobalSetup 建立的 sequence builder。</summary>
    private LayaSequenceBuilder GetBuilder()
    {
        return _builder ?? throw new InvalidOperationException("Benchmark setup did not initialize the builder.");
    }

    /// <summary>取得已由 GlobalSetup 建立的 post processor。</summary>
    private LayaPostProcessor GetPostProcessor()
    {
        return _postProcessor ?? throw new InvalidOperationException("Benchmark setup did not initialize the post processor.");
    }
}

/// <summary>集中建立 benchmark 的固定 question labels 與 state profiles。</summary>
file static class BenchmarkScenarioFactory
{
    /// <summary>取得三種 state profile 名稱。</summary>
    public static IReadOnlyList<string> StateProfiles { get; } = new[] { "short", "medium", "long" };

    /// <summary>建立指定 question 數與 state profile 的 request。</summary>
    public static LayaRequest CreateRequest(int questionCount, string stateProfile)
    {
        var labels = new[]
        {
            "food", "transport", "shopping", "utilities", "transfer", "salary", "bank_fee", "investment", "medical", "entertainment", "other"
        };
        var questions = Enumerable.Range(0, questionCount)
            .Select(index => index % 2 == 0
                ? new LayaQuestion($"category_{index}", LayaQuestionType.Choice, "Choose a category", labels)
                : new LayaQuestion($"needs_review_{index}", LayaQuestionType.Noul, "Is this uncertain?"))
            .ToArray();

        return new LayaRequest(GetState(stateProfile), questions);
    }

    /// <summary>建立只含四欄 synthetic transaction state 的 Choice＋Noul request。</summary>
    public static LayaRequest CreateTransactionRequest(int sequence)
    {
        var labels = new[]
        {
            "food", "transport", "shopping", "utilities", "transfer", "salary", "bank_fee", "investment", "medical", "entertainment", "other"
        };
        var questions = new LayaQuestion[]
        {
            new("category", LayaQuestionType.Choice, "Choose a category", labels),
            new("needs_review", LayaQuestionType.Noul, "Is this uncertain?")
        };
        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["description"] = "Benchmark transaction",
            ["amount"] = 420 + sequence,
            ["currency"] = "TWD",
            ["direction"] = sequence % 2 == 0 ? "debit" : "credit"
        };

        return new LayaRequest(state, questions);
    }

    /// <summary>建立不含真實交易資料的短、中、長 state。</summary>
    private static object GetState(string stateProfile)
    {
        var baseState = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["description"] = "Benchmark transaction",
            ["amount"] = 420,
            ["currency"] = "TWD",
            ["direction"] = "debit"
        };
        var repeatCount = stateProfile switch
        {
            "short" => 0,
            "medium" => 24,
            "long" => 220,
            _ => throw new ArgumentOutOfRangeException(nameof(stateProfile))
        };

        if (repeatCount > 0)
        {
            baseState["notes"] = string.Join(
                ' ',
                Enumerable.Repeat("synthetic benchmark state token", repeatCount));
        }

        return baseState;
    }
}
