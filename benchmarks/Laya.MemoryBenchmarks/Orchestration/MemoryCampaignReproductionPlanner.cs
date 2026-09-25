using Laya.MemoryBenchmarks.Analysis;

namespace Laya.MemoryBenchmarks.Orchestration;

/// <summary>依 frozen replicate tolerances 決定是否排程每組第三次獨立 run。</summary>
internal static class MemoryCampaignReproductionPlanner
{
    /// <summary>只為已超出 policy tolerance 的兩次 run 建立第三個 unique invocation。</summary>
    public static IReadOnlyList<MemoryProbeInvocation> PlanThirdReplicates(
        IReadOnlyList<MemoryProbeInvocation> plan,
        IReadOnlyDictionary<string, MemoryReplicateMeasurement> measurements,
        MemoryReplicateTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(tolerance);
        var thirdRuns = new List<MemoryProbeInvocation>();

        foreach (var group in plan
                     .Where(invocation => invocation.ReplicationGroup is not null)
                     .GroupBy(invocation => invocation.ReplicationGroup!, StringComparer.Ordinal))
        {
            var groupMeasurements = group
                .Select(invocation => measurements.TryGetValue(invocation.RunId, out var measurement)
                    ? measurement
                    : throw new InvalidDataException($"Replicate '{invocation.RunId}' has no raw memory summary."))
                .ToArray();
            var assessment = MemoryReplicateAnalyzer.Assess(groupMeasurements, tolerance);
            if (assessment.RequiresThirdRun)
            {
                if (groupMeasurements.Length != 2)
                {
                    throw new InvalidDataException(
                        $"Replicate group '{group.Key}' requested a third run after an unsupported initial count.");
                }

                thirdRuns.Add(MemoryCampaignPlanner.CreateThirdReplicate(group.First(), 3));
            }
        }

        return thirdRuns;
    }
}
