using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Laya.ConsoleApp.Evaluation;
using Laya.Core.Evaluation;
using Laya.Core.Exceptions;

namespace Laya.Core.Tests.Evaluation;

public sealed class Phase2DatasetSelectorTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    /// <summary>驗證固定資料依原始順序分成互斥且完整的兩組，並保存有序識別碼雜湊。</summary>
    [Fact]
    public void Select_RepositoryDataset_Has165DevelopmentAnd55HeldOutInCsvOrder()
    {
        using var fixture = new DatasetFixture();
        var development = new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath);
        var heldOut = new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath, "held-out");
        var rows = new TransactionCsvReader().Read(fixture.CsvPath, TransactionCsvPhase.Phase2);
        var expected = rows.Where(row => !row.Notes!.EndsWith("-04", StringComparison.Ordinal))
            .Select(row => row.Id).ToArray();

        Assert.Equal(165, development.Rows.Count);
        Assert.Equal(55, heldOut.Rows.Count);
        Assert.Equal(expected, development.SelectedIds);
        Assert.Equal(rows.Where(row => row.Notes!.EndsWith("-04", StringComparison.Ordinal))
            .Select(row => row.Id), heldOut.SelectedIds);
        Assert.Empty(development.SelectedIds.Intersect(heldOut.SelectedIds));
        Assert.Equal(220, development.SelectedIds.Concat(heldOut.SelectedIds).Distinct().Count());
        Assert.Equal(Hash(JsonSerializer.SerializeToUtf8Bytes(expected)), development.SelectedIdsHash);
        Assert.Equal("35225d5afaa85bade1d642edf4a34d02d06d5d486792d6f95e9e81b1eab7330d", development.SelectedIdsHash);
        Assert.Equal(Hash(File.ReadAllBytes(fixture.CsvPath)), development.DatasetHash);
        Assert.Equal(Hash(File.ReadAllBytes(fixture.GuidelinePath)), development.GuidelineHash);
        Assert.Equal(Hash(File.ReadAllBytes(fixture.ManifestPath)), development.ManifestHash);
    }

    /// <summary>驗證選取順序由 CSV 決定，群組及群組內識別碼順序不能改變結果。</summary>
    [Fact]
    public void Select_ReorderedCsv_ChangesOrderedHashAndPreservesCsvOrder()
    {
        using var fixture = new DatasetFixture();
        var original = new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath);
        var lines = File.ReadAllLines(fixture.CsvPath);
        File.WriteAllLines(fixture.CsvPath, lines.Take(1).Concat(lines.Skip(1).Reverse()));
        fixture.RefreshDatasetHash();
        var selection = new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath);

        Assert.Equal(original.SelectedIds.Reverse(), selection.SelectedIds);
        Assert.NotEqual(original.SelectedIdsHash, selection.SelectedIdsHash);
    }

    /// <summary>驗證 manifest 群組及識別碼排列不會取代 CSV 順序。</summary>
    [Fact]
    public void Select_ReorderedManifest_PreservesSelectedIdsAndHash()
    {
        using var fixture = new DatasetFixture();
        var original = new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath);
        var manifest = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!;
        var groups = new JsonObject();
        foreach (var entry in manifest["groups"]!.AsObject().Reverse())
        {
            var group = entry.Value!.DeepClone();
            group["ids"] = new JsonArray(group["ids"]!.AsArray().Reverse().Select(id => id!.DeepClone()).ToArray());
            groups.Add(entry.Key, group);
        }
        manifest["groups"] = groups;
        File.WriteAllText(fixture.ManifestPath, manifest.ToJsonString());

        var reordered = new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath);

        Assert.Equal(original.SelectedIds, reordered.SelectedIds);
        Assert.Equal(original.SelectedIdsHash, reordered.SelectedIdsHash);
        Assert.NotEqual(original.ManifestHash, reordered.ManifestHash);
    }

    /// <summary>驗證未知及全資料分組一律拒絕，不退回預設開發集合。</summary>
    [Theory]
    [InlineData("all")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("Development")]
    public void Select_InvalidSplit_IsRejected(string split)
    {
        using var fixture = new DatasetFixture();
        var error = Assert.Throws<LayaConfigurationException>(() =>
            new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath, split));
        Assert.Contains("split", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證所有群組及檔案契約，包括未被選取的保留集合，皆須先通過檢查。</summary>
    [Theory]
    [InlineData("dataset-hash", "dataset")]
    [InlineData("guideline-hash", "guideline")]
    [InlineData("missing-id", "coverage")]
    [InlineData("unknown-id", "unknown")]
    [InlineData("duplicate-id", "duplicate")]
    [InlineData("cross-group", "duplicate")]
    [InlineData("overlap", "duplicate")]
    [InlineData("group-split", "split")]
    [InlineData("group-category", "category")]
    [InlineData("group-membership", "membership")]
    [InlineData("empty-group", "empty")]
    [InlineData("row-count", "rowCount")]
    [InlineData("development-count", "developmentCount")]
    [InlineData("held-out-count", "heldOutCount")]
    [InlineData("missing-property", "guideline")]
    [InlineData("invalid-type", "rowCount")]
    public void Select_InvalidManifest_IsRejected(string mutation, string diagnostic)
    {
        using var fixture = new DatasetFixture();
        fixture.Mutate(mutation);
        var error = Assert.Throws<LayaConfigurationException>(() =>
            new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath));
        Assert.Contains(diagnostic, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證重複 JSON 群組名稱不得被反序列化覆蓋而掩蓋錯誤。</summary>
    [Fact]
    public void Select_DuplicateGroupName_IsRejected()
    {
        using var fixture = new DatasetFixture();
        var json = File.ReadAllText(fixture.ManifestPath);
        File.WriteAllText(fixture.ManifestPath, json.Replace("\"food-02\":", "\"food-01\":", StringComparison.Ordinal));
        var error = Assert.Throws<LayaConfigurationException>(() =>
            new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath));
        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證缺檔及無效 JSON 提供明確資料設定診斷。</summary>
    [Theory]
    [InlineData("manifest")]
    [InlineData("guideline")]
    [InlineData("csv")]
    [InlineData("json")]
    public void Select_UnreadableInput_IsRejected(string target)
    {
        using var fixture = new DatasetFixture();
        if (target == "json")
        {
            File.WriteAllText(fixture.ManifestPath, "{");
        }
        else
        {
            File.Delete(target == "manifest" ? fixture.ManifestPath :
                target == "guideline" ? fixture.GuidelinePath : fixture.CsvPath);
        }

        Assert.Throws<LayaConfigurationException>(() =>
            new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath));
    }

    /// <summary>驗證即使更新資料雜湊，重複 ID 或錯誤 CSV schema 仍然失敗。</summary>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("schema")]
    public void Select_InvalidCsvWithMatchingHash_IsRejected(string mutation)
    {
        using var fixture = new DatasetFixture();
        var csv = File.ReadAllText(fixture.CsvPath);
        File.WriteAllText(fixture.CsvPath, mutation == "duplicate"
            ? csv.Replace("phase2-002", "phase2-001", StringComparison.Ordinal)
            : csv.Replace("source_type", "invalid_column", StringComparison.Ordinal));
        fixture.RefreshDatasetHash();
        Assert.Throws<LayaConfigurationException>(() =>
            new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath));
    }

    /// <summary>以合成預測驗證開發集合的結果、指標及錯誤 CSV 不包含保留識別碼。</summary>
    [Fact]
    public void Select_Development_SyntheticReportsExcludeHeldOutIds()
    {
        using var fixture = new DatasetFixture();
        var selected = new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath);
        var heldOut = new Phase2DatasetSelector().Select(fixture.CsvPath, fixture.ManifestPath, "held-out");
        var predictions = selected.Rows.Select(CreateSyntheticPrediction).ToArray();
        var run = new LayaPhase2RunResult(selected.Rows.Count,
            new LayaPhase2RunManifest("selector-synthetic", "synthetic", "synthetic", selected.DatasetHash,
                "development", "A", "synthetic", null, DateTimeOffset.UnixEpoch, "synthetic test"),
            predictions, Array.Empty<LayaPhase2Failure>());
        var output = new Phase2ReportWriter().WriteRun(fixture.Root, run);
        var errorPath = Path.Combine(fixture.Root, "errors.csv");
        new MisclassifiedTransactionWriter().Write(errorPath, run, selected.Rows);
        using var metrics = JsonDocument.Parse(File.ReadAllText(output.MetricsPath));

        Assert.Equal(165, metrics.RootElement.GetProperty("metrics").GetProperty("InputCount").GetInt32());
        Assert.Equal(165, metrics.RootElement.GetProperty("metrics").GetProperty("SuccessCount").GetInt32());
        foreach (var path in new[] { output.ResultsPath, output.MetricsPath, errorPath })
        {
            var content = File.ReadAllText(path);
            Assert.All(heldOut.SelectedIds, id => Assert.DoesNotContain(id, content, StringComparison.Ordinal));
        }

        Assert.Equal(selected.SelectedIds, File.ReadAllLines(errorPath).Skip(1).Select(line => line.Split(',')[0]));
    }

    /// <summary>驗證 CLI 在缺少模型時仍優先回報非法資料，且不產生報告。</summary>
    [Theory]
    [InlineData("dataset-hash", "dataset")]
    [InlineData("guideline-hash", "guideline")]
    [InlineData("overlap", "duplicate")]
    [InlineData("missing-id", "coverage")]
    [InlineData("unknown-id", "unknown")]
    [InlineData("group-membership", "membership")]
    [InlineData("group-split", "split")]
    public async Task Cli_InvalidDataset_FailsBeforeEngineInitialization(string mutation, string diagnostic)
    {
        using var fixture = new DatasetFixture();
        fixture.Mutate(mutation);
        var result = await RunCli(fixture, "--evaluate-csv", fixture.CsvPath, "--phase", "phase2",
            "--dataset-manifest", fixture.ManifestPath);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(diagnostic, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Model path:", result.Error, StringComparison.Ordinal);
        AssertNoReports(fixture);
    }

    /// <summary>驗證 CLI 對全資料、未知 split 與缺值選項於模型載入前拒絕。</summary>
    [Theory]
    [InlineData("--split", "all", "split")]
    [InlineData("--split", "unknown", "split")]
    [InlineData("--dataset-manifest", null, "requires a value")]
    public async Task Cli_InvalidSelectionOption_FailsBeforeEngineInitialization(
        string option, string? value, string diagnostic)
    {
        using var fixture = new DatasetFixture();
        var args = new List<string> { "--evaluate-csv", fixture.CsvPath, "--phase", "phase2" };
        if (option != "--dataset-manifest")
        {
            args.AddRange(["--dataset-manifest", fixture.ManifestPath]);
        }
        args.Add(option);
        if (value is not null)
        {
            args.Add(value);
        }
        var result = await RunCli(fixture, args.ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(diagnostic, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Model path:", result.Error, StringComparison.Ordinal);
        AssertNoReports(fixture);
    }

    /// <summary>驗證尚未接通凍結候選契約時，保留集合一律明確阻擋且沒有輸出副作用。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cli_HeldOut_IsBlockedEvenWithCandidateFlag(bool supplyCandidate)
    {
        using var fixture = new DatasetFixture();
        var args = new List<string> { "--evaluate-csv", fixture.CsvPath, "--phase", "phase2",
            "--dataset-manifest", fixture.ManifestPath, "--split", "held-out" };
        if (supplyCandidate)
        {
            var candidatePath = Path.Combine(fixture.Root, "candidate.json");
            File.WriteAllText(candidatePath, "{}");
            args.AddRange(["--frozen-candidate", candidatePath]);
        }
        var result = await RunCli(fixture, args.ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("frozen-candidate", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Model path:", result.Error, StringComparison.Ordinal);
        AssertNoReports(fixture);
    }

    /// <summary>驗證固定預設 manifest 可用，而資料檔名不影響明確選擇的 Phase 1 七欄模式。</summary>
    [Theory]
    [InlineData("phase1")]
    [InlineData(null)]
    [InlineData("phase2")]
    public async Task Cli_ValidDataset_ReachesMissingModelOnlyAfterValidation(string? phase)
    {
        using var fixture = new DatasetFixture();
        var args = new List<string> { "--evaluate-csv", fixture.CsvPath };
        if (phase is not null)
        {
            args.AddRange(["--phase", phase]);
        }
        if (phase is null or "phase1")
        {
            File.WriteAllText(fixture.CsvPath,
                "id,description,amount,currency,direction,expected_category,language\n" +
                string.Join('\n', Enumerable.Range(1, 30).Select(index =>
                    $"id-{index},Synthetic,-1,TWD,debit,food,en")) + "\n");
            File.Delete(fixture.ManifestPath);
        }
        else
        {
            var defaultDirectory = Directory.CreateDirectory(Path.Combine(fixture.Root, "test-data"));
            File.Copy(fixture.GuidelinePath, Path.Combine(defaultDirectory.FullName, "category-label-guideline.md"));
            var manifest = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!;
            manifest["guideline"]!["path"] = "test-data/category-label-guideline.md";
            File.WriteAllText(Path.Combine(defaultDirectory.FullName, "transactions-phase2-manifest.json"), manifest.ToJsonString());
        }
        var result = await RunCli(fixture, args.ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Model path:", result.Error, StringComparison.Ordinal);
        AssertNoReports(fixture);
    }

    /// <summary>驗證兩種 Phase 的 CSV schema 都在模型載入前檢查，而未指定 Phase 時仍採七欄契約。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("phase1")]
    [InlineData("phase2")]
    public async Task Cli_InvalidCsvSchema_FailsBeforeEngineInitialization(string? phase)
    {
        using var fixture = new DatasetFixture();
        File.WriteAllText(fixture.CsvPath, "invalid_header\n");
        fixture.RefreshDatasetHash();
        var args = new List<string> { "--evaluate-csv", fixture.CsvPath,
            "--dataset-manifest", fixture.ManifestPath };
        if (phase is not null)
        {
            args.AddRange(["--phase", phase]);
        }
        var result = await RunCli(fixture, args.ToArray());
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("CSV header", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Model path:", result.Error, StringComparison.Ordinal);
        AssertNoReports(fixture);
    }

    /// <summary>建立刻意誤分類的合成預測，使錯誤 CSV 涵蓋每個選取識別碼。</summary>
    private static LayaPhase2Prediction CreateSyntheticPrediction(TransactionRow row)
    {
        var predicted = row.ExpectedCategory == "other" ? "food" : "other";
        return new LayaPhase2Prediction(row.Id, row.ExpectedCategory, predicted, row.Language,
            LayaPhase2Labels.Categories.ToDictionary(label => label, label => label == predicted ? 0.8 : 0.02),
            [predicted, row.ExpectedCategory], 0.8, 0.78, 0.2, null, null);
    }

    /// <summary>以不存在的模型路徑執行真實 CLI，僅測試前置流程而不執行推論。</summary>
    private static async Task<(int ExitCode, string Error)> RunCli(DatasetFixture fixture, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = fixture.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["DOTNET_ROLL_FORWARD"] = "Major";
        start.ArgumentList.Add(typeof(TransactionCsvReader).Assembly.Location);
        start.ArgumentList.Add("--model-root");
        start.ArgumentList.Add(Path.Combine(fixture.Root, "missing-model"));
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.Empty(await stdout);
        return (process.ExitCode, await stderr);
    }

    /// <summary>驗證失敗的前置流程沒有建立任何品質或錯誤報告。</summary>
    private static void AssertNoReports(DatasetFixture fixture)
    {
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "reports")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "misclassified-transactions.csv")));
    }

    /// <summary>依固定小寫十六進位格式計算測試所需的 SHA-256。</summary>
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>隔離資料及 manifest 的變異，不修改工作樹中的固定資料。</summary>
    private sealed class DatasetFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("laya-selector-").FullName;
        public string CsvPath => Path.Combine(Root, "transactions-phase2.csv");
        public string ManifestPath => Path.Combine(Root, "manifest.json");
        public string GuidelinePath => Path.Combine(Root, "guideline.md");
        private JsonObject Manifest { get; }

        /// <summary>複製固定資料及標註準則，使用明確絕對路徑避免依賴測試工作目錄。</summary>
        public DatasetFixture()
        {
            File.Copy(Path.Combine(RepositoryRoot, "test-data/transactions-phase2.csv"), CsvPath);
            File.Copy(Path.Combine(RepositoryRoot, "test-data/category-label-guideline.md"), GuidelinePath);
            Manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot,
                "test-data/transactions-phase2-manifest.json")))!.AsObject();
            Manifest["dataset"]!["path"] = CsvPath;
            Manifest["guideline"]!["path"] = GuidelinePath;
            Save();
        }

        /// <summary>建立單一契約錯誤，讓各負向測試驗證對應診斷。</summary>
        public void Mutate(string mutation)
        {
            var groups = Manifest["groups"]!;
            var first = groups["food-01"]!["ids"]!.AsArray();
            switch (mutation)
            {
                case "dataset-hash": Manifest["dataset"]!["sha256"] = new string('0', 64); break;
                case "guideline-hash": Manifest["guideline"]!["sha256"] = new string('0', 64); break;
                case "missing-id": groups["food-04"]!["ids"]!.AsArray().RemoveAt(0); break;
                case "unknown-id": first[0] = "unknown-id"; break;
                case "duplicate-id": first.Add("phase2-001"); break;
                case "cross-group": groups["food-02"]!["ids"]!.AsArray().Add("phase2-001"); break;
                case "overlap": groups["food-04"]!["ids"]!.AsArray().Add("phase2-001"); break;
                case "group-split": groups["food-04"]!["split"] = "all"; break;
                case "group-category": groups["food-01"]!["category"] = "transport"; break;
                case "group-membership":
                    first[0] = "phase2-006";
                    groups["food-02"]!["ids"]![0] = "phase2-001";
                    break;
                case "empty-group": groups["food-04"]!["ids"] = new JsonArray(); break;
                case "row-count": Manifest["dataset"]!["rowCount"] = 221; break;
                case "development-count": Manifest["split"]!["developmentCount"] = 164; break;
                case "held-out-count": Manifest["split"]!["heldOutCount"] = 54; break;
                case "missing-property": Manifest.Remove("guideline"); break;
                case "invalid-type": Manifest["dataset"]!["rowCount"] = "220"; break;
                default: throw new ArgumentException("未知測試變異。", nameof(mutation));
            }
            Save();
        }

        /// <summary>更新測試資料雜湊，使後續測試能抵達 CSV 或 membership 驗證。</summary>
        public void RefreshDatasetHash()
        {
            Manifest["dataset"]!["sha256"] = Hash(File.ReadAllBytes(CsvPath));
            Save();
        }

        /// <summary>保存目前測試 manifest。</summary>
        private void Save() => File.WriteAllText(ManifestPath, Manifest.ToJsonString());

        /// <summary>釋放測試產生的檔案及報告。</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
