using Laya.MemoryBenchmarks.Analysis;

namespace Laya.MemoryBenchmarks.Orchestration;

/// <summary>解析 MemoryBenchmarks campaign host CLI。</summary>
internal static class MemoryCampaignCommandLine
{
    private static readonly HashSet<string> SupportedStages = new(StringComparer.Ordinal)
    {
        "pilot", "baseline", "components", "lifecycle", "arena", "shape", "concurrency", "reproduction", "aggregate"
    };

    /// <summary>解析 stage、campaign ID、mode、policy、model override 與 resume flag。</summary>
    public static MemoryCampaignOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal) { "resume" };
        var valueOptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "stage", "campaign-id", "mode", "output", "model-root", "policy"
        };

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                throw new ArgumentException($"Unexpected campaign positional argument '{argument}'.", nameof(arguments));
            }

            var name = argument[2..];
            if (!flags.Contains(name) && !valueOptions.Contains(name))
            {
                throw new ArgumentException($"Unknown campaign option '{argument}'.", nameof(arguments));
            }

            if (values.ContainsKey(name))
            {
                throw new ArgumentException($"Campaign option '{argument}' cannot be repeated.", nameof(arguments));
            }

            if (flags.Contains(name))
            {
                values.Add(name, null);
                continue;
            }

            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Campaign option '{argument}' requires a value.", nameof(arguments));
            }

            values.Add(name, arguments[++index]);
        }

        var stage = GetRequired(values, "stage");
        if (!SupportedStages.Contains(stage))
        {
            throw new ArgumentException($"Unknown campaign stage '{stage}'.", nameof(arguments));
        }

        var campaignId = GetRequired(values, "campaign-id");
        ValidateCampaignId(campaignId);
        var mode = GetOptional(values, "mode") ?? "pilot";
        if (mode is not ("pilot" or "formal"))
        {
            throw new ArgumentException("Campaign mode must be pilot or formal.", nameof(arguments));
        }

        var output = GetOptional(values, "output") ?? "reports/phase3a";
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("Campaign output root cannot be empty.", nameof(arguments));
        }

        var modelRoot = GetOptional(values, "model-root");
        var policyPath = GetOptional(values, "policy");
        FrozenMemoryPolicy? policy = null;
        if (mode == "formal")
        {
            if (policyPath is null)
            {
                throw new ArgumentException("Formal campaign requires --policy with a frozen memory policy.", nameof(arguments));
            }

            policyPath = Path.GetFullPath(policyPath);
            policy = MemoryPolicyLoader.Load(policyPath);
        }
        else if (policyPath is not null)
        {
            policyPath = Path.GetFullPath(policyPath);
            policy = MemoryPolicyLoader.Load(policyPath);
        }

        return new MemoryCampaignOptions
        {
            Stage = stage,
            CampaignId = campaignId,
            Mode = mode,
            OutputRoot = Path.GetFullPath(output),
            ModelRoot = modelRoot is null ? null : Path.GetFullPath(modelRoot),
            PolicyPath = policyPath,
            Resume = values.ContainsKey("resume"),
            Policy = policy
        };
    }

    /// <summary>取得必要且非空 string option。</summary>
    private static string GetRequired(IReadOnlyDictionary<string, string?> values, string name)
    {
        var value = GetOptional(values, name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required campaign option '--{name}' is missing.", nameof(values));
    }

    /// <summary>取得 optional string option。</summary>
    private static string? GetOptional(IReadOnlyDictionary<string, string?> values, string name)
    {
        return values.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>驗證 campaign ID 是 3-80 字元安全 ASCII slug。</summary>
    private static void ValidateCampaignId(string campaignId)
    {
        if (campaignId.Length is < 3 or > 80 ||
            !char.IsAsciiLetterOrDigit(campaignId[0]) ||
            campaignId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new ArgumentException("Campaign ID must be a 3-80 character ASCII slug.", nameof(campaignId));
        }
    }
}
