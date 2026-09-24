using System.Security.Cryptography;
using System.Text.Json;
using Laya.Core.Exceptions;

namespace Laya.ConsoleApp.Evaluation;

/// <summary>保存已驗證的選取列、有序識別碼及資料來源雜湊。</summary>
public sealed record Phase2DatasetSelection(
    string Split,
    IReadOnlyList<TransactionRow> Rows,
    IReadOnlyList<string> SelectedIds,
    string SelectedIdsHash,
    string DatasetHash,
    string GuidelineHash,
    string ManifestHash);

/// <summary>在模型初始化前驗證 Phase 2 資料並依 manifest 選取分組。</summary>
public sealed class Phase2DatasetSelector
{
    /// <summary>
    /// 驗證完整資料集合，並按 CSV 原始順序回傳指定分組。
    /// manifest 的相對 guideline 路徑以工作目錄為基準，與既有儲存庫路徑契約一致；
    /// CSV 以呼叫者指定的路徑及 manifest 雜湊核對，不依檔名猜測 phase。
    /// 有序 ID 雜湊採 System.Text.Json 預設緊密字串陣列的 UTF-8 位元組，無 BOM 或結尾換行。
    /// </summary>
    public Phase2DatasetSelection Select(string csvPath, string manifestPath, string split = "development")
    {
        ValidateSplit(split);
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        try
        {
            var manifestBytes = File.ReadAllBytes(manifestPath);
            using var document = JsonDocument.Parse(manifestBytes);
            var manifest = document.RootElement;
            ValidateUniqueProperties(manifest, "manifest");
            var dataset = RequireProperty(manifest, "dataset", JsonValueKind.Object);
            var guideline = RequireProperty(manifest, "guideline", JsonValueKind.Object);
            var splits = RequireProperty(manifest, "split", JsonValueKind.Object);
            var groups = RequireProperty(manifest, "groups", JsonValueKind.Object);
            var datasetHash = VerifyFileHash(csvPath, RequireString(dataset, "sha256"), "dataset");
            var guidelineHash = VerifyFileHash(
                RequireString(guideline, "path"), RequireString(guideline, "sha256"), "guideline");
            var rows = new TransactionCsvReader().Read(csvPath, TransactionCsvPhase.Phase2);
            ValidateCount(dataset, "rowCount", rows.Count);
            var membership = ValidateMembership(rows, groups);
            ValidateCount(splits, "developmentCount", membership.Values.Count(value => value == "development"));
            ValidateCount(splits, "heldOutCount", membership.Values.Count(value => value == "held-out"));
            var selected = rows.Where(row => membership[row.Id] == split).ToArray();
            var ids = selected.Select(row => row.Id).ToArray();

            return new Phase2DatasetSelection(split, Array.AsReadOnly(selected), Array.AsReadOnly(ids),
                Hash(JsonSerializer.SerializeToUtf8Bytes(ids)), datasetHash, guidelineHash, Hash(manifestBytes));
        }
        catch (JsonException)
        {
            throw new LayaConfigurationException($"Phase 2 dataset manifest is invalid JSON: {manifestPath}");
        }
        catch (IOException exception)
        {
            throw new LayaConfigurationException($"Phase 2 dataset input could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new LayaConfigurationException($"Phase 2 dataset input could not be read: {exception.Message}");
        }
    }

    /// <summary>只接受明確支援的兩個 split，避免未知值或 all 退回全資料推論。</summary>
    private static void ValidateSplit(string split)
    {
        if (split is not ("development" or "held-out"))
        {
            throw new LayaConfigurationException(
                $"Unknown Phase 2 split '{split}'; expected development or held-out; all is not supported.");
        }
    }

    /// <summary>驗證每個識別碼恰屬一個群組、分類一致且所有 CSV 列均受完整覆蓋。</summary>
    private static IReadOnlyDictionary<string, string> ValidateMembership(
        IReadOnlyList<TransactionRow> rows, JsonElement groups)
    {
        var rowById = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var membership = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                throw new LayaConfigurationException("Phase 2 group name must not be empty.");
            }

            var groupSplit = RequireString(group.Value, "split");
            ValidateSplit(groupSplit);
            var category = RequireString(group.Value, "category");
            var ids = RequireProperty(group.Value, "ids", JsonValueKind.Array);
            if (ids.GetArrayLength() == 0)
            {
                throw new LayaConfigurationException($"Phase 2 group '{group.Name}' has an empty ids list.");
            }

            foreach (var idElement in ids.EnumerateArray())
            {
                if (idElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idElement.GetString()))
                {
                    throw new LayaConfigurationException($"Phase 2 group '{group.Name}' has an invalid ID.");
                }

                var id = idElement.GetString()!;
                if (!membership.TryAdd(id, groupSplit))
                {
                    throw new LayaConfigurationException(
                        $"Phase 2 duplicate ID '{id}' within/across groups or splits.");
                }

                if (!rowById.TryGetValue(id, out var row))
                {
                    throw new LayaConfigurationException($"Phase 2 group '{group.Name}' contains unknown ID '{id}'.");
                }

                if (!string.Equals(row.ExpectedCategory, category, StringComparison.Ordinal))
                {
                    throw new LayaConfigurationException($"Phase 2 group '{group.Name}' category mismatch for ID '{id}'.");
                }

                // notes 仍為自由文字；若包含既有 group= 標記，必須與 manifest membership 相符。
                var declaredGroups = (row.Notes ?? string.Empty).Split(';', StringSplitOptions.TrimEntries)
                    .Where(note => note.StartsWith("group=", StringComparison.Ordinal)).ToArray();
                if (declaredGroups.Length > 0 &&
                    (declaredGroups.Length != 1 || declaredGroups[0] != $"group={group.Name}"))
                {
                    throw new LayaConfigurationException($"Phase 2 group membership mismatch for ID '{id}'.");
                }
            }
        }

        if (membership.Count != rows.Count)
        {
            var missing = rows.First(row => !membership.ContainsKey(row.Id));
            throw new LayaConfigurationException($"Phase 2 group coverage is incomplete; missing ID '{missing.Id}'.");
        }

        return membership;
    }

    /// <summary>拒絕重複 JSON 屬性，避免群組名稱或其他契約欄位遭最後一值覆蓋。</summary>
    private static void ValidateUniqueProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new LayaConfigurationException($"Phase 2 duplicate manifest property '{path}.{property.Name}'.");
                }
                ValidateUniqueProperties(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateUniqueProperties(item, path);
            }
        }
    }

    /// <summary>讀取必要 JSON 欄位並以明確診斷拒絕缺漏、空值或錯誤型別。</summary>
    private static JsonElement RequireProperty(JsonElement parent, string name, JsonValueKind kind)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
        {
            throw new LayaConfigurationException($"Phase 2 manifest property '{name}' must be {kind}.");
        }
        return value;
    }

    /// <summary>讀取必要的非空字串，不自動修剪或變更識別值。</summary>
    private static string RequireString(JsonElement parent, string name)
    {
        var value = RequireProperty(parent, name, JsonValueKind.String).GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LayaConfigurationException($"Phase 2 manifest property '{name}' must not be empty.");
        }
        return value;
    }

    /// <summary>以實際資料或完整分組計數核對 manifest 的整數計數。</summary>
    private static void ValidateCount(JsonElement parent, string name, int actual)
    {
        var value = RequireProperty(parent, name, JsonValueKind.Number);
        if (!value.TryGetInt32(out var expected) || expected != actual)
        {
            throw new LayaConfigurationException($"Phase 2 manifest '{name}' mismatch; actual count is {actual}.");
        }
    }

    /// <summary>以串流計算完整檔案雜湊並拒絕不符的資料或準則版本。</summary>
    private static string VerifyFileHash(string path, string expected, string kind)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new LayaConfigurationException($"Phase 2 {kind} SHA-256 mismatch: {path}");
        }
        return actual;
    }

    /// <summary>以小寫十六進位編碼保存 manifest 及有序 ID 位元組的 SHA-256。</summary>
    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
