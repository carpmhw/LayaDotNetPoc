using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Laya.MemoryBenchmarks.Analysis;

/// <summary>載入並嚴格驗證在 pilot 後凍結的 Phase 3A memory policy。</summary>
internal static class MemoryPolicyLoader
{
    private const long RequiredTargetBytes = 3_000_000_000;

    private static readonly IReadOnlyDictionary<string, int> RequiredIntegers =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["baselineRuns"] = 2,
            ["runOnlyRuns"] = 2,
            ["arenaOnRuns"] = 2,
            ["arenaOffRuns"] = 2,
            ["dockerTargetRuns"] = 2,
            ["repetitionsBeforeThirdRun"] = 2,
            ["baselineRequests"] = 5000,
            ["runOnlyRequests"] = 5000,
            ["arenaRequests"] = 5000,
            ["tokenizerRequestsPerLanguage"] = 10000,
            ["sequenceOnlyRequests"] = 10000,
            ["tensorOnlyRequests"] = 10000,
            ["calibrationOnlyRequests"] = 10000,
            ["shapeAndConcurrencyRequests"] = 1000,
            ["dockerExtendedRequests"] = 5000
        };

    private static readonly IReadOnlyList<string> RequiredMatrixProperties =
        RequiredIntegers.Keys.Concat(new[]
        {
            "dockerLimitsBytes", "dockerSmokeRequests", "dockerExtendedLimitsBytes", "sessionRecreateCycles",
            "shapeProfiles", "questionCounts", "concurrencyLevels"
        }).ToArray();

    /// <summary>讀取 frozen policy，拒絕隱含 threshold、錯誤 target 與不完整 matrix。</summary>
    public static FrozenMemoryPolicy Load(string policyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyPath);
        if (!File.Exists(policyPath))
        {
            throw new FileNotFoundException($"Frozen memory policy was not found: {policyPath}", policyPath);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(policyPath));
            var root = document.RootElement;
            EnsureObject(root, "memory policy");
            EnsureProperties(
                root,
                new[]
                {
                    "schemaVersion", "policyId", "status", "frozenUtc", "freezeCommit", "pilotRunIds",
                    "pilotArtifactSha256", "targetMemoryLimitBytes", "rationale", "limitations", "thresholds", "minimumMatrix"
                },
                "memory policy");

            if (root.GetProperty("schemaVersion").GetInt32() != 1)
            {
                throw Invalid("schemaVersion must equal 1.");
            }

            var policyId = ReadRequiredString(root, "policyId");
            if (policyId.Length is < 3 or > 64 || policyId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                throw Invalid("policyId must be a 3-64 character kebab-case ID.");
            }

            if (!string.Equals(root.GetProperty("status").GetString(), "frozen", StringComparison.Ordinal))
            {
                throw Invalid("Only a policy with status 'frozen' is accepted for formal analysis.");
            }

            if (!DateTimeOffset.TryParse(
                    root.GetProperty("frozenUtc").GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var frozenUtc))
            {
                throw Invalid("frozenUtc must be an ISO-8601 timestamp.");
            }

            var freezeCommit = ReadRequiredString(root, "freezeCommit");
            if (freezeCommit.Length < 7)
            {
                throw Invalid("freezeCommit must identify a source commit.");
            }

            var pilotRunIds = root.GetProperty("pilotRunIds").EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray();
            if (pilotRunIds.Length == 0 || pilotRunIds.Any(string.IsNullOrWhiteSpace) ||
                pilotRunIds.Distinct(StringComparer.Ordinal).Count() != pilotRunIds.Length)
            {
                throw Invalid("pilotRunIds must contain unique non-empty run IDs.");
            }

            var pilotHash = ReadSha256(root.GetProperty("pilotArtifactSha256"), "pilotArtifactSha256");
            var targetBytes = root.GetProperty("targetMemoryLimitBytes").GetInt64();
            if (targetBytes != RequiredTargetBytes)
            {
                throw Invalid("targetMemoryLimitBytes must remain 3,000,000,000 decimal bytes.");
            }

            var rationale = ReadRequiredString(root, "rationale");
            if (rationale.Length < 20)
            {
                throw Invalid("rationale must document the pilot and deployment budget used to freeze thresholds.");
            }

            var limitations = ReadRequiredString(root, "limitations");
            var thresholds = root.GetProperty("thresholds");
            EnsureProperties(
                thresholds,
                new[] { "privateMemory", "rss", "managedHeap", "nearLinearR2Minimum", "replicateTolerance" },
                "thresholds");
            var privateMemory = ReadSlopeBudget(thresholds.GetProperty("privateMemory"), "privateMemory");
            var rss = ReadSlopeBudget(thresholds.GetProperty("rss"), "rss");
            var managedHeap = ReadSlopeBudget(thresholds.GetProperty("managedHeap"), "managedHeap");
            var r2Minimum = ReadFiniteDouble(thresholds.GetProperty("nearLinearR2Minimum"), "nearLinearR2Minimum");
            if (r2Minimum is < 0 or > 1)
            {
                throw Invalid("nearLinearR2Minimum must be between zero and one.");
            }

            var replicateTolerance = ReadReplicateTolerance(thresholds.GetProperty("replicateTolerance"));
            var minimumMatrix = ReadMinimumMatrix(root.GetProperty("minimumMatrix"));
            return new FrozenMemoryPolicy(
                policyId,
                frozenUtc,
                freezeCommit,
                pilotRunIds,
                pilotHash,
                targetBytes,
                rationale,
                limitations,
                privateMemory,
                rss,
                managedHeap,
                r2Minimum,
                replicateTolerance,
                minimumMatrix,
                HashFile(policyPath));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Frozen memory policy '{policyPath}' is invalid JSON or has invalid types.", exception);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidDataException($"Frozen memory policy '{policyPath}' is missing a required field.", exception);
        }
    }

    /// <summary>解析 slope 與 growth budgets，拒絕負值或額外 property。</summary>
    private static MemorySlopeBudget ReadSlopeBudget(JsonElement element, string name)
    {
        EnsureProperties(
            element,
            new[] { "maximumLateSlopeBytesPer100Requests", "lateGrowthBudgetBytes" },
            name);
        var slope = ReadFiniteDouble(element.GetProperty("maximumLateSlopeBytesPer100Requests"), name);
        var growth = element.GetProperty("lateGrowthBudgetBytes").GetInt64();
        if (slope < 0 || growth < 0)
        {
            throw Invalid($"{name} slope and growth budgets must be non-negative.");
        }

        return new MemorySlopeBudget(slope, growth);
    }

    /// <summary>解析三項 replicate tolerances 並拒絕負值或額外 property。</summary>
    private static MemoryReplicateTolerance ReadReplicateTolerance(JsonElement element)
    {
        EnsureProperties(
            element,
            new[] { "peakRelativeDifferenceMaximum", "endRelativeDifferenceMaximum", "slopeRelativeDifferenceMaximum" },
            "replicateTolerance");
        var peak = ReadFiniteDouble(element.GetProperty("peakRelativeDifferenceMaximum"), "peakRelativeDifferenceMaximum");
        var end = ReadFiniteDouble(element.GetProperty("endRelativeDifferenceMaximum"), "endRelativeDifferenceMaximum");
        var slope = ReadFiniteDouble(element.GetProperty("slopeRelativeDifferenceMaximum"), "slopeRelativeDifferenceMaximum");
        if (peak < 0 || end < 0 || slope < 0)
        {
            throw Invalid("replicate tolerances must be non-negative.");
        }

        return new MemoryReplicateTolerance(peak, end, slope);
    }

    /// <summary>驗證 matrix 所有固定 counts、limits、cycles、profiles 與 question shapes。</summary>
    private static MemoryMinimumMatrix ReadMinimumMatrix(JsonElement element)
    {
        EnsureProperties(element, RequiredMatrixProperties, "minimumMatrix");
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var item in RequiredIntegers)
        {
            var actual = element.GetProperty(item.Key).GetInt32();
            if (actual != item.Value)
            {
                throw Invalid($"minimumMatrix.{item.Key} must equal {item.Value}.");
            }

            values[item.Key] = actual;
        }

        AddExpectedLongs(element, values, "dockerLimitsBytes", new long[] { 2_000_000_000, 2_500_000_000, 3_000_000_000, 4_000_000_000 });
        AddExpectedInts(element, values, "dockerSmokeRequests", new[] { 100, 1000 });
        AddExpectedLongs(element, values, "dockerExtendedLimitsBytes", new long[] { 3_000_000_000, 4_000_000_000 });
        AddExpectedInts(element, values, "sessionRecreateCycles", new[] { 10, 100 });
        AddExpectedStrings(element, values, "shapeProfiles", new[] { "short", "medium", "long" });
        AddExpectedInts(element, values, "questionCounts", new[] { 1, 2, 5 });
        AddExpectedInts(element, values, "concurrencyLevels", new[] { 1, 2, 4 });
        return new MemoryMinimumMatrix(new ReadOnlyDictionary<string, object>(values));
    }

    /// <summary>驗證 JSON long array 與 frozen values 完全一致。</summary>
    private static void AddExpectedLongs(JsonElement parent, IDictionary<string, object> values, string name, long[] expected)
    {
        var actual = parent.GetProperty(name).EnumerateArray().Select(item => item.GetInt64()).ToArray();
        if (!actual.SequenceEqual(expected))
        {
            throw Invalid($"minimumMatrix.{name} does not match the required campaign limits.");
        }

        values[name] = actual;
    }

    /// <summary>驗證 JSON int array 與 frozen values 完全一致。</summary>
    private static void AddExpectedInts(JsonElement parent, IDictionary<string, object> values, string name, int[] expected)
    {
        var actual = parent.GetProperty(name).EnumerateArray().Select(item => item.GetInt32()).ToArray();
        if (!actual.SequenceEqual(expected))
        {
            throw Invalid($"minimumMatrix.{name} does not match the required campaign matrix.");
        }

        values[name] = actual;
    }

    /// <summary>驗證 JSON string array 與 frozen profile values 完全一致。</summary>
    private static void AddExpectedStrings(JsonElement parent, IDictionary<string, object> values, string name, string[] expected)
    {
        var actual = parent.GetProperty(name).EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw Invalid($"minimumMatrix.{name} does not match the required campaign matrix.");
        }

        values[name] = actual;
    }

    /// <summary>拒絕 non-object、missing properties 與未定義 additional properties。</summary>
    private static void EnsureProperties(JsonElement element, IEnumerable<string> names, string objectName)
    {
        EnsureObject(element, objectName);
        var expected = new HashSet<string>(names, StringComparer.Ordinal);
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        var extra = actual.Except(expected, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || extra.Length > 0)
        {
            throw Invalid($"{objectName} fields are invalid; missing=[{string.Join(',', missing)}], extra=[{string.Join(',', extra)}].");
        }
    }

    /// <summary>確認 JSON element 是 object。</summary>
    private static void EnsureObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{name} must be a JSON object.");
        }
    }

    /// <summary>取得必要 non-empty string 欄位。</summary>
    private static string ReadRequiredString(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw Invalid($"{name} must be a non-empty string.");
    }

    /// <summary>讀取 finite JSON number。</summary>
    private static double ReadFiniteDouble(JsonElement element, string name)
    {
        var value = element.GetDouble();
        return double.IsFinite(value) ? value : throw Invalid($"{name} must be finite.");
    }

    /// <summary>驗證 lowercase SHA-256 hex 字串。</summary>
    private static string ReadSha256(JsonElement element, string name)
    {
        var value = element.GetString();
        return value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : throw Invalid($"{name} must be a lowercase SHA-256 hex string.");
    }

    /// <summary>計算 policy file 實際 bytes 的 SHA-256。</summary>
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>建立一致的 invalid policy diagnostic exception。</summary>
    private static InvalidDataException Invalid(string message)
    {
        return new InvalidDataException($"Frozen memory policy is invalid: {message}");
    }
}
