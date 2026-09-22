using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.Core.PostProcessing;
using Laya.Core.Serialization;
using Laya.Core.Tokenization;

var modelRoot = Path.GetFullPath(GetOption(args, "--model-root") ?? Path.Combine("models", "laya"));
var benchmarkArgs = RemoveOptions(args, "--model-root", "--cold-start", "--metadata", "--collect");
Environment.SetEnvironmentVariable("LAYA_MODEL_ROOT", modelRoot);

if (HasFlag(args, "--cold-start"))
{
    RunColdStart(modelRoot);
    return;
}

if (HasFlag(args, "--metadata"))
{
    PrintMetadata(modelRoot);
    return;
}

if (HasFlag(args, "--collect"))
{
    RunCollection(modelRoot);
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

/// <summary>判斷 command-line flag 是否存在。</summary>
static bool HasFlag(IReadOnlyList<string> args, string name)
{
    return args.Any(argument => string.Equals(argument, name, StringComparison.Ordinal));
}

/// <summary>移除 application-specific options，避免 BenchmarkDotNet 將其視為未知參數。</summary>
static string[] RemoveOptions(IReadOnlyList<string> args, params string[] names)
{
    var nameSet = new HashSet<string>(names, StringComparer.Ordinal);
    var filtered = new List<string>(args.Count);
    for (var index = 0; index < args.Count; index++)
    {
        if (nameSet.Contains(args[index]))
        {
            if (args[index] == "--model-root")
            {
                index++;
            }

            continue;
        }

        filtered.Add(args[index]);
    }

    return filtered.ToArray();
}

/// <summary>在獨立程序中量測 model load、首次 Run 與載入前後記憶體。</summary>
static void RunColdStart(string modelRoot)
{
    var process = Process.GetCurrentProcess();
    process.Refresh();
    var workingSetBefore = process.WorkingSet64;
    var managedBefore = GC.GetTotalMemory(forceFullCollection: true);

    var loadStopwatch = Stopwatch.StartNew();
    using var engine = LayaDecisionEngine.Open(modelRoot);
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
        mode = "cold-start",
        processId = Environment.ProcessId,
        modelRoot,
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

    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>輸出九組 benchmark 的實際 token 長度與截斷狀態。</summary>
static void PrintMetadata(string modelRoot)
{
    var bundle = LayaModelValidator.ValidateBundle(modelRoot);
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

    Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>以五次 warmup 與一百次觀測收集九組 Run-only／end-to-end 統計。</summary>
static void RunCollection(string modelRoot)
{
    var bundle = LayaModelValidator.ValidateBundle(modelRoot);
    using var session = LayaOnnxSession.Open(modelRoot);
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
                Summarize(runOnly),
                Summarize(endToEnd),
                runOnly,
                endToEnd));
        }
    }

    process.Refresh();
    var output = new
    {
        mode = "warm-collection",
        modelRoot,
        warmupCount = 5,
        sampleCount = 100,
        processWorkingSetBeforeBytes = workingSetBefore,
        processWorkingSetAfterBytes = process.WorkingSet64,
        peakWorkingSetBytes = peakWorkingSet,
        managedBeforeBytes = managedBefore,
        managedAfterBytes = GC.GetTotalMemory(forceFullCollection: true),
        percentileAlgorithm = "linear interpolation on sorted samples at (n-1)*p",
        scenarios = results
    };

    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>計算 mean、P50、P95 與 P99，使用排序樣本的線性插值。</summary>
static DistributionSummary Summarize(IReadOnlyList<double> values)
{
    var sorted = values.OrderBy(value => value).ToArray();
    return new DistributionSummary(
        Math.Round(sorted.Average(), 3),
        Math.Round(Percentile(sorted, 0.50), 3),
        Math.Round(Percentile(sorted, 0.95), 3),
        Math.Round(Percentile(sorted, 0.99), 3),
        Math.Round(sorted.Min(), 3),
        Math.Round(sorted.Max(), 3));
}

/// <summary>以線性插值取得排序樣本的指定分位數。</summary>
static double Percentile(IReadOnlyList<double> sortedValues, double probability)
{
    var position = (sortedValues.Count - 1) * probability;
    var lower = (int)Math.Floor(position);
    var upper = (int)Math.Ceiling(position);
    if (lower == upper)
    {
        return sortedValues[lower];
    }

    var fraction = position - lower;
    return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
}

/// <summary>描述一組 request 的實際 token 與截斷 metadata。</summary>
file sealed record ScenarioMetadata(
    int QuestionCount,
    string StateProfile,
    int StateTokenCount,
    int SequenceLength,
    int MarkerCount,
    bool WasTruncated);

/// <summary>保存單一 latency 經驗分布的統計結果。</summary>
file sealed record DistributionSummary(
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MinMs,
    double MaxMs);

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
    DistributionSummary RunOnly,
    DistributionSummary EndToEnd,
    IReadOnlyList<double> RunOnlySamples,
    IReadOnlyList<double> EndToEndSamples);

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
        _session = LayaOnnxSession.Open(
            Path.GetFullPath(Environment.GetEnvironmentVariable("LAYA_MODEL_ROOT") ?? ModelRootOverride));
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
