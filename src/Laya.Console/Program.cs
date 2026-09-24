using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Laya.Core.Configuration;
using Laya.Core.Evaluation;
using Laya.Core.Exceptions;
using Laya.Core.Inference;
using Laya.Core.Models;
using Laya.ConsoleApp.Evaluation;
using Laya.Shared.Configuration;

return Run(args);

/// <summary>執行不洩漏交易原文的 Laya demo console。</summary>
static int Run(string[] args)
{
    try
    {
        var csvPath = GetOption(args, "--evaluate-csv");
        var phase = ParsePhase(GetOption(args, "--phase"));
        var promptVariant = ParsePromptVariant(GetOption(args, "--prompt"));
        var split = GetOption(args, "--split") ?? "development";
        var frozenCandidatePath = GetOption(args, "--frozen-candidate");
        var datasetManifestPath = GetOption(args, "--dataset-manifest")
            ?? Path.Combine("test-data", "transactions-phase2-manifest.json");
        IReadOnlyList<TransactionRow>? rows = null;
        Phase2DatasetSelection? selection = null;
        LayaFrozenCandidate? frozenCandidate = null;
        var profile = ResolveProfile(args);
        if (csvPath is not null)
        {
            if (phase == TransactionCsvPhase.Phase2)
            {
                var selector = new Phase2DatasetSelector();
                selection = selector.Select(csvPath, datasetManifestPath, split);
                if (selection.Split == "held-out")
                {
                    if (frozenCandidatePath is null)
                    {
                        throw new LayaConfigurationException(
                            "Phase 2 held-out is blocked: requires --frozen-candidate with a validated development policy.");
                    }

                    var development = selector.Select(csvPath, datasetManifestPath, "development");
                    frozenCandidate = new Phase2FrozenCandidateValidator().Validate(
                        frozenCandidatePath,
                        Directory.GetCurrentDirectory(),
                        profile,
                        development,
                        selection,
                        Path.Combine("test-data", "multilingual-reference-manifest.json"),
                        promptVariant);
                }
                else if (frozenCandidatePath is not null)
                {
                    throw new LayaConfigurationException(
                        "--frozen-candidate is only valid with --split held-out.");
                }
                rows = selection.Rows;
            }
            else
            {
                if (frozenCandidatePath is not null)
                {
                    throw new LayaConfigurationException(
                        "--frozen-candidate is only valid for Phase 2 held-out evaluation.");
                }

                rows = ReadTransactions(csvPath, phase);
            }
        }
        using var engine = LayaDecisionEngine.Open(new LayaOptions(
            profile.ModelRoot,
            profile.Name,
            profile.CheckpointRevision));
        if (HasFlag(args, "--smoke"))
        {
            RunSmoke(engine, GetPositiveIntOption(args, "--smoke-count", 100));
            return 0;
        }

        if (rows is not null)
        {
            RunEvaluation(
                engine,
                rows,
                selection,
                profile,
                promptVariant,
                frozenCandidate,
                frozenCandidatePath,
                string.Join(' ', args));
        }
        else
        {
            PrintResult(engine.Decide(CreateDemoRequest()), engine.LoadDuration);
        }

        return 0;
    }
    catch (LayaException exception)
    {
        Console.Error.WriteLine(
            $"Laya failed at stage '{exception.Stage}': {exception.Message}");
        PrintDiagnostics(exception);

        return 2;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Unexpected Laya failure: {exception.Message}");
        return 3;
    }
}

/// <summary>判斷 command line 是否包含指定的無值 flag。</summary>
static bool HasFlag(IReadOnlyList<string> args, string name)
{
    return args.Any(argument => string.Equals(argument, name, StringComparison.Ordinal));
}

/// <summary>解析正整數 option，供 smoke request 數量使用。</summary>
static int GetPositiveIntOption(IReadOnlyList<string> args, string name, int fallback)
{
    var value = GetOption(args, name);
    if (value is null)
    {
        return fallback;
    }

    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
    {
        throw new LayaConfigurationException($"Command-line option '{name}' must be a positive integer.");
    }

    return parsed;
}

/// <summary>在同一長生命週期 engine 執行 Choice、Noul 與重複請求 smoke mode。</summary>
static void RunSmoke(LayaDecisionEngine engine, int requestCount)
{
    var process = Process.GetCurrentProcess();
    var workingSetSeries = new List<long>(requestCount);
    var successes = 0;
    var failures = 0;
    var stopwatch = Stopwatch.StartNew();
    for (var index = 0; index < requestCount; index++)
    {
        try
        {
            _ = engine.Decide(CreateDemoRequest());
            successes++;
        }
        catch (Exception)
        {
            failures++;
        }

        process.Refresh();
        workingSetSeries.Add(process.WorkingSet64);
    }

    stopwatch.Stop();
    var output = new
    {
        mode = "phase2-smoke",
        requestCount,
        successes,
        failures,
        elapsedMs = stopwatch.Elapsed.TotalMilliseconds,
        peakWorkingSetBytes = workingSetSeries.Count == 0 ? 0 : workingSetSeries.Max(),
        workingSetSeries
    };
    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    if (failures > 0)
    {
        throw new LayaConfigurationException($"Smoke mode completed with {failures} failed requests.");
    }
}

/// <summary>解析明確的資料 phase，未指定時維持 Phase 1 相容入口。</summary>
static TransactionCsvPhase ParsePhase(string? value)
{
    if (value is null)
    {
        return TransactionCsvPhase.Phase1;
    }

    return value switch
    {
        "phase1" => TransactionCsvPhase.Phase1,
        "phase2" => TransactionCsvPhase.Phase2,
        _ => throw new LayaConfigurationException(
            $"Unknown transaction phase '{value}'; expected phase1 or phase2.")
    };
}

/// <summary>解析 Phase 2 僅允許的 A、B、C prompt variant。</summary>
static LayaPromptVariant ParsePromptVariant(string? value)
{
    return value?.ToUpperInvariant() switch
    {
        null or "A" => LayaPromptVariant.A,
        "B" => LayaPromptVariant.B,
        "C" => LayaPromptVariant.C,
        _ => throw new LayaConfigurationException(
            $"Unknown prompt variant '{value}'; expected A, B or C.")
    };
}

/// <summary>解析 host 設定與 command line 的模型 profile。</summary>
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

/// <summary>取得需要值的 command-line option；缺值時立即回報使用錯誤。</summary>
static string? GetOption(IReadOnlyList<string> args, string name)
{
    for (var index = 0; index < args.Count; index++)
    {
        if (!string.Equals(args[index], name, StringComparison.Ordinal))
        {
            continue;
        }

        if (index == args.Count - 1 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new LayaConfigurationException($"Command-line option '{name}' requires a value.");
        }

        return args[index + 1];
    }

    return null;
}

/// <summary>建立含 11 類 category 與 needs_review Noul 的合成 request。</summary>
static LayaRequest CreateDemoRequest()
{
    var categories = new[]
    {
        "food",
        "transport",
        "shopping",
        "utilities",
        "transfer",
        "salary",
        "bank_fee",
        "investment",
        "medical",
        "entertainment",
        "other"
    };

    var state = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["description"] = "Uber Eats Taiwan",
        ["amount"] = -580,
        ["currency"] = "TWD",
        ["direction"] = "debit"
    };

    return new LayaRequest(
        state,
        new[]
        {
            new LayaQuestion(
                "category",
                LayaQuestionType.Choice,
                "Choose the best transaction category",
                categories),
            new LayaQuestion(
                "needs_review",
                LayaQuestionType.Noul,
                "Is this transaction uncertain?")
        });
}

/// <summary>輸出答案、分布與 Run-only latency，不輸出完整交易 state。</summary>
static void PrintResult(LayaResult result, TimeSpan loadDuration)
{
    var category = result.Answers.Single(answer => answer.Name == "category");
    var needsReview = result.Answers.Single(answer => answer.Name == "needs_review");
    var ranked = category.Probabilities
        .OrderByDescending(item => item.Value)
        .ThenBy(item => item.Key, StringComparer.Ordinal)
        .ToArray();
    var top1 = ranked[0];
    var top2 = ranked[1];
    var pTrue = needsReview.Probabilities["true"];
    var output = new
    {
        category = new
        {
            selectedOption = category.SelectedOption,
            probabilities = category.Probabilities,
            top1 = new { label = top1.Key, probability = Math.Round(top1.Value, 4) },
            top2 = new { label = top2.Key, probability = Math.Round(top2.Value, 4) },
            margin = Math.Round(top1.Value - top2.Value, 4)
        },
        needsReview = new
        {
            selectedOption = needsReview.SelectedOption,
            pTrue = Math.Round(pTrue, 4),
            probabilities = needsReview.Probabilities
        },
        questionCount = result.Answers.Count,
        modelLoadDurationMs = Math.Round(loadDuration.TotalMilliseconds, 3),
        answers = result.Answers.Select(answer => new
        {
            answer.Name,
            answer.SelectedOption,
            probability = Math.Round(answer.Probability, 4),
            probabilities = answer.Probabilities.ToDictionary(
                item => item.Key,
                item => Math.Round(item.Value, 4),
                StringComparer.Ordinal)
        }),
        inferenceDurationMs = Math.Round(result.InferenceDuration.TotalMilliseconds, 3)
    };

    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>輸出可用結構化診斷欄位，不輸出 inner exception 或交易原文。</summary>
static void PrintDiagnostics(LayaException exception)
{
    if (exception.ModelPath is not null)
    {
        Console.Error.WriteLine($"Model path: {exception.ModelPath}");
    }

    if (exception.QuestionName is not null)
    {
        Console.Error.WriteLine($"Question: {exception.QuestionName}");
    }

    if (exception.SequenceLength is not null)
    {
        Console.Error.WriteLine($"Sequence length: {exception.SequenceLength}");
    }

    if (exception.OptionCount is not null)
    {
        Console.Error.WriteLine($"Option count: {exception.OptionCount}");
    }
}

/// <summary>執行 CSV evaluation，輸出 summary、每類指標、confusion matrix 與 coverage。</summary>
static void RunEvaluation(
    LayaDecisionEngine engine,
    IReadOnlyList<TransactionRow> rows,
    Phase2DatasetSelection? selection,
    LayaProfileResolution profile,
    LayaPromptVariant promptVariant,
    LayaFrozenCandidate? frozenCandidate,
    string? frozenCandidatePath,
    string command)
{
    if (selection is not null)
    {
        var run = new Phase2EvaluationRunner(engine).Run(
            rows,
            profile.Name,
            profile.CheckpointRevision ?? "unverified",
            selection.DatasetHash,
            selection.Split,
            promptVariant,
            policyName: frozenCandidate?.Policy.Name,
            command: command,
            selection: selection,
            bundleManifestSha256: HashFileIfPresent(Path.Combine(profile.ModelRoot, "laya-bundle-manifest.json")),
            referenceManifestSha256: HashFileIfPresent(Path.Combine("test-data", "multilingual-reference-manifest.json")),
            fixedPolicy: frozenCandidate?.Policy,
            frozenCandidateManifestSha256: frozenCandidate?.ManifestSha256,
            fixtureHash: HashFileIfPresent(Path.Combine("test-data", "multilingual-parity-fixtures.json")),
            frozenCandidatePath: frozenCandidatePath);
        var phaseOutput = new Phase2ReportWriter().WriteRun(
            "reports",
            run,
            frozenCandidate?.Policy,
            frozenCandidate?.ManifestSha256);
        new MisclassifiedTransactionWriter().Write("misclassified-transactions.csv", run, rows);
        Console.WriteLine(JsonSerializer.Serialize(phaseOutput, new JsonSerializerOptions { WriteIndented = true }));
        if (!run.Complete)
        {
            throw new LayaConfigurationException(
                $"Phase 2 run '{run.Manifest.RunId}' is incomplete; successful subset cannot be readiness evidence.");
        }
        return;
    }

    var labels = TransactionCsvReader.CategoryLabels;
    var samples = new List<LayaEvaluationSample>(rows.Count);
    var modes = new Dictionary<LayaDecisionMode, int>();
    var failures = new List<TransactionFailure>();

    foreach (var row in rows)
    {
        try
        {
            var result = engine.Decide(CreateTransactionRequest(row, labels));
            var category = result.Answers.Single(answer => answer.Name == "category");
            var probabilities = category.Probabilities.Values.OrderByDescending(value => value).ToArray();
            var margin = probabilities.Length > 1 ? probabilities[0] - probabilities[1] : probabilities[0];
            var mode = LayaConfidencePolicy.Classify(category.Probability, margin);
            modes[mode] = modes.GetValueOrDefault(mode) + 1;
            samples.Add(new LayaEvaluationSample(
                row.Id,
                row.ExpectedCategory,
                category.SelectedOption ?? string.Empty,
                category.Probability,
                margin,
                row.Language,
                result.InferenceDuration.TotalMilliseconds));
        }
        catch (LayaException exception)
        {
            failures.Add(new TransactionFailure(row.Id, exception.Stage, exception.GetType().Name));
        }
        catch (Exception exception)
        {
            failures.Add(new TransactionFailure(row.Id, "unexpected", exception.GetType().Name));
        }
    }

    var report = samples.Count == 0 ? null : LayaEvaluationMetrics.Calculate(samples, labels);
    object? confidence = samples.Count == 0
        ? null
        : new
        {
            probabilityMin = Math.Round(samples.Min(sample => sample.Probability), 4),
            probabilityMean = Math.Round(samples.Average(sample => sample.Probability), 4),
            probabilityMax = Math.Round(samples.Max(sample => sample.Probability), 4),
            marginMin = Math.Round(samples.Min(sample => sample.Margin), 4),
            marginMean = Math.Round(samples.Average(sample => sample.Margin), 4),
            marginMax = Math.Round(samples.Max(sample => sample.Margin), 4)
        };
    var latencyValues = samples
        .Where(sample => sample.LatencyMilliseconds is not null)
        .Select(sample => sample.LatencyMilliseconds!.Value)
        .ToArray();
    object? latency = latencyValues.Length == 0
        ? null
        : new
        {
            sampleCount = latencyValues.Length,
            minMs = Math.Round(latencyValues.Min(), 3),
            meanMs = Math.Round(latencyValues.Average(), 3),
            maxMs = Math.Round(latencyValues.Max(), 3)
        };
    var output = new
    {
        mode = "csv-evaluation",
        totalCount = rows.Count,
        successCount = samples.Count,
        failureCount = failures.Count,
        complete = failures.Count == 0,
        failures,
        correctCount = report?.CorrectCount,
        overallAccuracy = report is null ? (double?)null : Math.Round(report.OverallAccuracy, 4),
        confidence,
        latency,
        decisionModes = modes.ToDictionary(item => item.Key.ToString().ToUpperInvariant(), item => item.Value),
        byLanguage = report?.ByLanguage.ToDictionary(
            item => item.Key,
            item => new
            {
                item.Value.SampleCount,
                item.Value.CorrectCount,
                accuracy = Math.Round(item.Value.Accuracy, 4)
            },
            StringComparer.Ordinal),
        perCategory = report?.PerClass.ToDictionary(
            item => item.Key,
            item => new
            {
                item.Value.Support,
                item.Value.PredictedCount,
                accuracy = RoundNullable(item.Value.Accuracy),
                precision = RoundNullable(item.Value.Precision),
                recall = RoundNullable(item.Value.Recall),
                f1 = RoundNullable(item.Value.F1)
            },
            StringComparer.Ordinal),
        confusionMatrix = report?.ConfusionMatrix,
        thresholds = report?.Thresholds.ToDictionary(
            item => item.Key.ToString("0.00", CultureInfo.InvariantCulture),
            item => new
            {
                coverage = Math.Round(item.Value.Coverage, 4),
                accuracy = RoundNullable(item.Value.Accuracy),
                item.Value.IncludedCount
            },
            StringComparer.Ordinal)
    };

    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>委派 phase-aware CSV reader 讀取交易列，避免 entrypoint 自行解析 CSV。</summary>
static IReadOnlyList<TransactionRow> ReadTransactions(string path, TransactionCsvPhase phase)
{
    return new TransactionCsvReader().Read(path, phase);
}

/// <summary>把一筆 transaction row 轉成同時包含 Choice 與 Noul 的 request。</summary>
static LayaRequest CreateTransactionRequest(TransactionRow row, IReadOnlyList<string> labels)
{
    var state = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["description"] = row.Description,
        ["amount"] = row.Amount,
        ["currency"] = row.Currency,
        ["direction"] = row.Direction
    };

    return new LayaRequest(
        state,
        new[]
        {
            new LayaQuestion("category", LayaQuestionType.Choice, "Choose the best transaction category", labels),
            new LayaQuestion("needs_review", LayaQuestionType.Noul, "Is this transaction uncertain?")
        });
}

/// <summary>將 nullable metric 四捨五入，保留空值代表沒有可計算分母。</summary>
static double? RoundNullable(double? value)
{
    return value is null ? null : Math.Round(value.Value, 4);
}

/// <summary>以串流 hash 讀取存在的 artifact，缺少時回傳 null。</summary>
static string? HashFileIfPresent(string path)
{
    if (!File.Exists(path))
    {
        return null;
    }

    using var stream = File.OpenRead(path);
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
}

/// <summary>保存單筆 evaluation 失敗的非敏感診斷摘要。</summary>
file sealed record TransactionFailure(string Id, string Stage, string ErrorType);
