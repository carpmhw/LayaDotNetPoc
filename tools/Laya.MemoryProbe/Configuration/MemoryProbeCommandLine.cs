using System.Globalization;
using Laya.MemoryBenchmarks.Analysis;
using Laya.Shared.Configuration;

namespace Laya.MemoryProbe.Configuration;

/// <summary>解析並驗證 Laya MemoryProbe 命令列，確保錯誤在載入模型前回報。</summary>
internal static class MemoryProbeCommandLine
{
    private const int MaximumRequestsPerRun = 100_000;
    private static readonly IReadOnlyDictionary<string, MemoryProbeScenario> Scenarios =
        new Dictionary<string, MemoryProbeScenario>(StringComparer.Ordinal)
        {
            ["tokenizer-only"] = MemoryProbeScenario.TokenizerOnly,
            ["sequence-only"] = MemoryProbeScenario.SequenceOnly,
            ["tensor-only"] = MemoryProbeScenario.TensorOnly,
            ["run-only"] = MemoryProbeScenario.RunOnly,
            ["calibration-only"] = MemoryProbeScenario.CalibrationOnly,
            ["full-pipeline"] = MemoryProbeScenario.FullPipeline,
            ["session-recreate"] = MemoryProbeScenario.SessionRecreate
        };

    private static readonly HashSet<int> SupportedGcCheckpoints = new() { 100, 500, 1000, 5000 };
    private static readonly HashSet<int> SupportedQuestionCounts = new() { 1, 2, 5 };

    /// <summary>解析 CLI 並拒絕無效組態；本方法不載入模型或建立 native 資源。</summary>
    public static MemoryProbeOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = ParseArguments(arguments);
        var isLoadOnly = values.ContainsKey("load-only");
        var mode = ParseMode(values.GetValueOrDefault("mode", "pilot"));
        var profileName = values.GetValueOrDefault("profile", "multilingual");
        if (!string.Equals(profileName, "multilingual", StringComparison.Ordinal))
        {
            throw new ArgumentException("Phase 3A MemoryProbe requires --profile multilingual.", nameof(arguments));
        }

        var modelRoot = values.GetValueOrDefault("model-root");
        if (modelRoot is not null && string.IsNullOrWhiteSpace(modelRoot))
        {
            throw new ArgumentException("--model-root cannot be empty.", nameof(arguments));
        }

        var outputValue = values.GetValueOrDefault("output", "reports/phase3a");
        if (string.IsNullOrWhiteSpace(outputValue))
        {
            throw new ArgumentException("--output cannot be empty.", nameof(arguments));
        }

        var outputDirectory = Path.GetFullPath(outputValue);
        var runId = values.GetValueOrDefault("run-id") ?? CreateRunId();
        ValidateRunId(runId);

        if (isLoadOnly)
        {
            ValidateLoadOnlyArguments(values, mode);
            return new MemoryProbeOptions
            {
                IsLoadOnly = true,
                Requests = 0,
                Concurrency = 1,
                SampleEvery = 50,
                ProfileName = profileName,
                ModelRootOverride = modelRoot,
                OutputDirectory = outputDirectory,
                RunId = runId,
                CpuArenaEnabled = ParseOnOff(values.GetValueOrDefault("cpu-arena", "on"), "cpu-arena"),
                WarmupRequests = 0,
                StateProfile = "short",
                QuestionCount = 1,
                IdleSeconds = Array.Empty<int>(),
                GcCheckpoints = Array.Empty<int>(),
                WorkloadId = string.Empty,
                PolicyPath = ResolveOptionalPolicyPath(values, mode),
                Mode = mode
            };
        }

        var scenarioName = GetRequired(values, "scenario");
        if (!Scenarios.TryGetValue(scenarioName, out var scenario))
        {
            throw new ArgumentException($"Unknown memory probe scenario '{scenarioName}'.", nameof(arguments));
        }

        var requests = ParsePositiveInteger(values, "requests");
        if (requests > MaximumRequestsPerRun)
        {
            throw new ArgumentException($"--requests cannot exceed {MaximumRequestsPerRun} for a bounded latency window.", nameof(arguments));
        }

        var concurrency = ParsePositiveInteger(values, "concurrency", defaultValue: 1);
        var sampleEvery = ParsePositiveInteger(values, "sample-every", defaultValue: 50);
        var cpuArena = ParseOnOff(values.GetValueOrDefault("cpu-arena", "on"), "cpu-arena");
        var warmup = ParseWarmup(values, scenario);
        var stateProfile = values.GetValueOrDefault("state", "short");
        ValidateStateProfile(stateProfile);
        var questionCount = ParseInteger(values, "questions", defaultValue: 1);
        if (!SupportedQuestionCounts.Contains(questionCount))
        {
            throw new ArgumentException("--questions must be 1, 2 or 5.", nameof(arguments));
        }

        var idleSeconds = ParseIntegerList(values.GetValueOrDefault("idle-seconds", "30,60,300"), "idle-seconds", allowZero: true);
        var idleOmissionReason = values.GetValueOrDefault("idle-omission-reason");
        if (mode == MemoryProbeMode.Formal && !idleSeconds.Contains(300) && string.IsNullOrWhiteSpace(idleOmissionReason))
        {
            throw new ArgumentException(
                "Formal mode requires --idle-omission-reason when --idle-seconds omits 300.",
                nameof(arguments));
        }

        var gcCheckpoints = ParseIntegerList(values.GetValueOrDefault("gc-checkpoints", string.Empty), "gc-checkpoints", allowZero: false);
        if (gcCheckpoints.Any(checkpoint => !SupportedGcCheckpoints.Contains(checkpoint) || checkpoint > requests))
        {
            throw new ArgumentException("--gc-checkpoints accepts only request counts 100, 500, 1000 or 5000 not exceeding --requests.", nameof(arguments));
        }

        if (scenario is not (MemoryProbeScenario.FullPipeline or MemoryProbeScenario.RunOnly) && concurrency != 1)
        {
            throw new ArgumentException(
                "Only full-pipeline and run-only support concurrency greater than one; other scenarios require --concurrency 1.",
                nameof(arguments));
        }

        var workloadId = values.GetValueOrDefault("workload", ResolveDefaultWorkload(scenario));
        if (string.IsNullOrWhiteSpace(workloadId))
        {
            throw new ArgumentException("--workload cannot be empty.", nameof(arguments));
        }

        var stateSchedule = ParseStateSchedule(values, scenario, requests);
        var policyPath = ResolveOptionalPolicyPath(values, mode);

        return new MemoryProbeOptions
        {
            Scenario = scenario,
            Requests = requests,
            Concurrency = concurrency,
            SampleEvery = sampleEvery,
            CpuArenaEnabled = cpuArena,
            WarmupRequests = warmup,
            StateProfile = stateProfile,
            QuestionCount = questionCount,
            IdleSeconds = idleSeconds,
            IdleOmissionReason = idleOmissionReason,
            GcCheckpoints = gcCheckpoints,
            OutputDirectory = outputDirectory,
            RunId = runId,
            ProfileName = profileName,
            ModelRootOverride = modelRoot,
            WorkloadId = workloadId,
            PolicyPath = policyPath,
            Mode = mode,
            StateSchedule = stateSchedule
        };
    }

    /// <summary>依共用 profile resolver 解析 multilingual model root 並驗證 profile 身分。</summary>
    public static LayaProfileResolution ResolveModel(
        MemoryProbeOptions options,
        string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var resolution = LayaProfileResolver.Resolve(
            options.ModelRootOverride,
            options.ProfileName,
            LayaProfileResolver.LoadEnvironment(),
            workingDirectory);
        if (!string.Equals(resolution.Name, "multilingual", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Resolved profile '{resolution.Name}' does not satisfy Phase 3A multilingual scope.");
        }

        return resolution;
    }

    /// <summary>將 switch 與 value 轉成字典，並拒絕重複、未知或缺值參數。</summary>
    private static Dictionary<string, string> ParseArguments(IReadOnlyList<string> arguments)
    {
        var valueOptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "scenario", "requests", "concurrency", "sample-every", "cpu-arena", "warmup", "state",
            "questions", "idle-seconds", "gc-checkpoints", "output", "run-id", "profile", "model-root",
            "idle-omission-reason", "workload", "state-schedule", "policy", "mode"
        };
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                throw new ArgumentException($"Unexpected positional argument '{argument}'.", nameof(arguments));
            }

            var name = argument[2..];
            if (name == "load-only")
            {
                if (!parsed.TryAdd(name, string.Empty))
                {
                    throw new ArgumentException("--load-only cannot be repeated.", nameof(arguments));
                }

                continue;
            }

            if (!valueOptions.Contains(name))
            {
                throw new ArgumentException($"Unknown option '{argument}'.", nameof(arguments));
            }

            if (!parsed.TryAdd(name, string.Empty))
            {
                throw new ArgumentException($"Option '{argument}' cannot be repeated.", nameof(arguments));
            }

            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{argument}' requires a value.", nameof(arguments));
            }

            parsed[name] = arguments[++index];
        }

        return parsed;
    }

    /// <summary>驗證 load-only 僅含 profile、路徑與 evidence 選項。</summary>
    private static void ValidateLoadOnlyArguments(
        IReadOnlyDictionary<string, string> values,
        MemoryProbeMode mode)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "load-only", "profile", "model-root", "output", "run-id", "policy", "mode", "cpu-arena"
        };
        var unsupported = values.Keys.FirstOrDefault(key => !allowed.Contains(key));
        if (unsupported is not null)
        {
            throw new ArgumentException($"--load-only cannot be combined with --{unsupported}.", nameof(values));
        }

        if (mode == MemoryProbeMode.Formal && !values.ContainsKey("policy"))
        {
            throw new ArgumentException("Formal mode requires --policy with a frozen memory policy.", nameof(values));
        }
    }

    /// <summary>取得必要參數，缺少時以參數名稱回報。</summary>
    private static string GetRequired(IReadOnlyDictionary<string, string> values, string name)
    {
        return values.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Required option '--{name}' is missing.", nameof(values));
    }

    /// <summary>解析必須大於零的整數參數。</summary>
    private static int ParsePositiveInteger(
        IReadOnlyDictionary<string, string> values,
        string name,
        int? defaultValue = null)
    {
        var value = values.TryGetValue(name, out var configured)
            ? ParseInteger(configured, name)
            : defaultValue ?? ParseInteger(GetRequired(values, name), name);
        return value > 0
            ? value
            : throw new ArgumentException($"--{name} must be greater than zero.", nameof(values));
    }

    /// <summary>解析整數參數並使用 invariant culture。</summary>
    private static int ParseInteger(
        IReadOnlyDictionary<string, string> values,
        string name,
        int? defaultValue = null)
    {
        return values.TryGetValue(name, out var value)
            ? ParseInteger(value, name)
            : defaultValue ?? throw new ArgumentException($"Required option '--{name}' is missing.", nameof(values));
    }

    /// <summary>解析並拒絕格式無效的 CLI 整數文字。</summary>
    private static int ParseInteger(string value, string name)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Option '--{name}' must be an integer.", nameof(value));
    }

    /// <summary>解析 session-recreate 專用 warmup，其他 scenario 要求至少五次。</summary>
    private static int ParseWarmup(
        IReadOnlyDictionary<string, string> values,
        MemoryProbeScenario scenario)
    {
        var warmup = values.ContainsKey("warmup")
            ? ParseInteger(values, "warmup")
            : scenario == MemoryProbeScenario.SessionRecreate ? 0 : 5;
        var isValid = scenario == MemoryProbeScenario.SessionRecreate
            ? warmup == 0
            : warmup >= 5;
        return isValid
            ? warmup
            : throw new ArgumentException(
                scenario == MemoryProbeScenario.SessionRecreate
                    ? "session-recreate requires --warmup 0."
                    : "Non-recreate scenarios require at least five warmup requests.",
                nameof(values));
    }

    /// <summary>解析 CPU arena 的 on／off 文字選項。</summary>
    private static bool ParseOnOff(string value, string name)
    {
        return value switch
        {
            "on" => true,
            "off" => false,
            _ => throw new ArgumentException($"--{name} must be 'on' or 'off'.", nameof(value))
        };
    }

    /// <summary>解析逗號分隔的非負或正整數清單。</summary>
    private static IReadOnlyList<int> ParseIntegerList(string value, string name, bool allowZero)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<int>();
        }

        var results = value.Split(',', StringSplitOptions.TrimEntries)
            .Select(item => ParseInteger(item, name))
            .ToArray();
        if (results.Any(item => allowZero ? item < 0 : item <= 0))
        {
            throw new ArgumentException($"--{name} contains an out-of-range value.", nameof(value));
        }

        if (results.Distinct().Count() != results.Length)
        {
            throw new ArgumentException($"--{name} cannot contain duplicate values.", nameof(value));
        }

        return results;
    }

    /// <summary>驗證固定 state profile 名稱。</summary>
    private static void ValidateStateProfile(string stateProfile)
    {
        if (stateProfile is not ("short" or "medium" or "long"))
        {
            throw new ArgumentException("--state must be short, medium or long.", nameof(stateProfile));
        }
    }

    /// <summary>解析 schedule 並確認 scenario、互斥 state 與 request count 相符。</summary>
    private static IReadOnlyList<StateScheduleSegment> ParseStateSchedule(
        IReadOnlyDictionary<string, string> values,
        MemoryProbeScenario scenario,
        int requests)
    {
        if (!values.TryGetValue("state-schedule", out var scheduleText))
        {
            return Array.Empty<StateScheduleSegment>();
        }

        if (scenario != MemoryProbeScenario.FullPipeline)
        {
            throw new ArgumentException("--state-schedule is only valid for full-pipeline.", nameof(values));
        }

        if (values.ContainsKey("state"))
        {
            throw new ArgumentException("--state-schedule and --state are mutually exclusive.", nameof(values));
        }

        var segments = scheduleText.Split(',', StringSplitOptions.TrimEntries)
            .Select(segment =>
            {
                var parts = segment.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    throw new ArgumentException("Each state schedule entry must use state:requests.", nameof(values));
                }

                ValidateStateProfile(parts[0]);
                var segmentRequests = ParseInteger(parts[1], "state-schedule");
                if (segmentRequests <= 0)
                {
                    throw new ArgumentException("State schedule request counts must be positive.", nameof(values));
                }

                return new StateScheduleSegment(parts[0], segmentRequests);
            })
            .ToArray();
        if (segments.Sum(segment => (long)segment.Requests) != requests)
        {
            throw new ArgumentException("State schedule request counts must sum to --requests.", nameof(values));
        }

        return segments;
    }

    /// <summary>解析 pilot／formal 執行模式。</summary>
    private static MemoryProbeMode ParseMode(string mode)
    {
        return mode switch
        {
            "pilot" => MemoryProbeMode.Pilot,
            "formal" => MemoryProbeMode.Formal,
            _ => throw new ArgumentException("--mode must be pilot or formal.", nameof(mode))
        };
    }

    /// <summary>依 scenario 選擇對應的預設 fixture ID。</summary>
    private static string ResolveDefaultWorkload(MemoryProbeScenario scenario)
    {
        return scenario switch
        {
            MemoryProbeScenario.TokenizerOnly => "tokenizer-en",
            MemoryProbeScenario.CalibrationOnly => "calibration-mixed-english-reference",
            _ => "phase3a-short-transaction"
        };
    }

    /// <summary>取得 optional policy 的絕對路徑並驗證正式模式需要 frozen policy 檔案。</summary>
    private static string? ResolveOptionalPolicyPath(
        IReadOnlyDictionary<string, string> values,
        MemoryProbeMode mode)
    {
        if (!values.TryGetValue("policy", out var policyPath))
        {
            if (mode == MemoryProbeMode.Formal)
            {
                throw new ArgumentException("Formal mode requires --policy with a frozen memory policy.", nameof(values));
            }

            return null;
        }

        var resolvedPath = Path.GetFullPath(policyPath);
        if (!File.Exists(resolvedPath))
        {
            throw new ArgumentException($"Memory policy file was not found: {resolvedPath}", nameof(values));
        }

        _ = MemoryPolicyLoader.Load(resolvedPath);

        return resolvedPath;
    }

    /// <summary>建立不易碰撞且只含安全路徑字元的預設 run ID。</summary>
    private static string CreateRunId()
    {
        return $"{DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
    }

    /// <summary>拒絕可穿越 run 輸出目錄的 run ID。</summary>
    private static void ValidateRunId(string runId)
    {
        if (runId.Length is < 3 or > 128 ||
            !char.IsAsciiLetterOrDigit(runId[0]) ||
            runId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
        {
            throw new ArgumentException("--run-id must be 3-128 safe ASCII letters, digits, dots, underscores or hyphens.", nameof(runId));
        }
    }
}
