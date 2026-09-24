using Laya.Core.Exceptions;

namespace Laya.Core.Evaluation;

/// <summary>保存兩次 Phase 2 run 的可比較共同成功集合與全輸入分母。</summary>
public sealed record LayaPhase2RunComparison(
    int LeftInputCount,
    int RightInputCount,
    IReadOnlyList<string> CommonSuccessfulIds);

/// <summary>驗證不同 model run 使用同一評估輸入契約後才建立共同子集。</summary>
public static class LayaPhase2RunComparer
{
    /// <summary>拒絕評估輸入或 runtime 契約不一致，並回傳共同成功 ID。</summary>
    public static LayaPhase2RunComparison Compare(
        LayaPhase2RunManifest left,
        IReadOnlyCollection<string> leftSuccessfulIds,
        LayaPhase2RunManifest right,
        IReadOnlyCollection<string> rightSuccessfulIds)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(leftSuccessfulIds);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(rightSuccessfulIds);

        RequireEqual(left.DatasetHash, right.DatasetHash, "datasetHash");
        RequireEqual(left.GuidelineHash, right.GuidelineHash, "guidelineHash");
        RequireEqual(left.DatasetManifestHash, right.DatasetManifestHash, "datasetManifestHash");
        RequireEqual(left.Split, right.Split, "split");
        RequireEqual(left.SelectedIdsHash, right.SelectedIdsHash, "selectedIdsHash");
        RequireSequenceEqual(left.SelectedIds, right.SelectedIds, "selectedIds");
        RequireEqual(left.PromptVariant, right.PromptVariant, "promptVariant");
        RequireEqual(left.PromptHash, right.PromptHash, "promptHash");
        RequireEqual(left.OptionOrderHash, right.OptionOrderHash, "optionOrderHash");
        RequireEqual(left.SerializationVersion, right.SerializationVersion, "serializationVersion");
        RequireEqual(left.SerializationHash, right.SerializationHash, "serializationHash");
        RequireEqual(left.EnvironmentFingerprint, right.EnvironmentFingerprint, "environmentFingerprint");
        if (left.ThreadCount != right.ThreadCount)
        {
            throw new LayaConfigurationException("Phase 2 run comparison mismatch at threadCount.");
        }

        var selected = left.SelectedIds?.ToHashSet(StringComparer.Ordinal)
            ?? throw new LayaConfigurationException("Phase 2 run is missing selectedIds provenance.");
        var leftSuccess = ValidateSuccessfulIds(leftSuccessfulIds, selected, "left");
        var rightSuccess = ValidateSuccessfulIds(rightSuccessfulIds, selected, "right");
        var common = leftSuccess.Intersect(rightSuccess, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return new LayaPhase2RunComparison(selected.Count, selected.Count, common);
    }

    /// <summary>驗證成功集合只包含本次 run 選取的 IDs 且無重複。</summary>
    private static IReadOnlySet<string> ValidateSuccessfulIds(
        IReadOnlyCollection<string> ids,
        IReadOnlySet<string> selected,
        string side)
    {
        var unique = ids.ToHashSet(StringComparer.Ordinal);
        if (unique.Count != ids.Count || unique.Any(id => !selected.Contains(id)))
        {
            throw new LayaConfigurationException(
                $"Phase 2 {side} successful IDs are not a unique subset of selected IDs.");
        }

        return unique;
    }

    /// <summary>比較必要的 scalar provenance 欄位，不允許 null 欄位悄悄互相匹配。</summary>
    private static void RequireEqual(string? left, string? right, string field)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) ||
            !string.Equals(left, right, StringComparison.Ordinal))
        {
            throw new LayaConfigurationException($"Phase 2 run comparison mismatch at {field}.");
        }
    }

    /// <summary>比較有序 selected IDs，保留 CSV 順序契約。</summary>
    private static void RequireSequenceEqual(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right,
        string field)
    {
        if (left is null || right is null || !left.SequenceEqual(right, StringComparer.Ordinal))
        {
            throw new LayaConfigurationException($"Phase 2 run comparison mismatch at {field}.");
        }
    }
}
