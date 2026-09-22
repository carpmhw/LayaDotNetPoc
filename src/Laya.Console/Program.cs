using System.Globalization;
using System.Text.Json;
using Laya.Core.Evaluation;
using Laya.Core.Exceptions;
using Laya.Core.Inference;
using Laya.Core.Models;

return Run(args);

/// <summary>執行不洩漏交易原文的 Laya demo console。</summary>
static int Run(string[] args)
{
    var modelRoot = GetModelRoot(args);

    try
    {
        using var engine = LayaDecisionEngine.Open(modelRoot);
        var csvPath = GetOption(args, "--evaluate-csv");
        if (csvPath is not null)
        {
            RunEvaluation(engine, csvPath);
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

/// <summary>解析 --model-root；未指定時使用 repository 預設 bundle 路徑。</summary>
static string GetModelRoot(IReadOnlyList<string> args)
{
    return GetOption(args, "--model-root") ?? Path.Combine("models", "laya");
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
static void RunEvaluation(LayaDecisionEngine engine, string csvPath)
{
    var rows = ReadTransactions(csvPath);
    var labels = new[]
    {
        "food", "transport", "shopping", "utilities", "transfer", "salary", "bank_fee", "investment", "medical", "entertainment", "other"
    };
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

/// <summary>從 CSV 讀取固定欄位的 transaction rows；資料列不包含原文輸出。</summary>
static IReadOnlyList<TransactionRow> ReadTransactions(string path)
{
    if (!File.Exists(path))
    {
        throw new LayaConfigurationException($"Transaction CSV was not found: {path}");
    }

    var lines = File.ReadAllLines(path);
    if (lines.Length == 0 || lines[0] != "id,description,amount,currency,direction,expected_category,language")
    {
        throw new LayaConfigurationException("Transaction CSV has an unexpected header.");
    }

    var rows = new List<TransactionRow>();
    for (var lineNumber = 1; lineNumber < lines.Length; lineNumber++)
    {
        if (string.IsNullOrWhiteSpace(lines[lineNumber]))
        {
            continue;
        }

        var columns = lines[lineNumber].Split(',');
        if (columns.Length != 7)
        {
            throw new LayaConfigurationException($"Transaction CSV row {lineNumber + 1} must contain 7 columns.");
        }

        if (!double.TryParse(columns[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            throw new LayaConfigurationException($"Transaction CSV row {lineNumber + 1} has an invalid amount.");
        }

        rows.Add(new TransactionRow(
            columns[0],
            columns[1],
            amount,
            columns[3],
            columns[4],
            columns[5],
            columns[6]));
    }

    if (rows.Count is < 30 or > 50)
    {
        throw new LayaConfigurationException("Transaction CSV must contain 30-50 data rows.");
    }

    return rows;
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

/// <summary>保存 CSV transaction 的已解析欄位。</summary>
file sealed record TransactionRow(
    string Id,
    string Description,
    double Amount,
    string Currency,
    string Direction,
    string ExpectedCategory,
    string Language);

/// <summary>保存單筆 evaluation 失敗的非敏感診斷摘要。</summary>
file sealed record TransactionFailure(string Id, string Stage, string ErrorType);
