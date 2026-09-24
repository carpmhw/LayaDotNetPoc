using System.Security.Cryptography;
using System.Text.Json;
using Laya.ConsoleApp.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class Phase2DatasetManifestTests
{
    /// <summary>驗證 dataset/guideline hash、group ID 完整性與 split 不跨 group。</summary>
    [Fact]
    public void Manifest_MatchesRepositoryDatasetAndHasUniqueGroupSplit()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var datasetPath = Path.Combine(root, "test-data", "transactions-phase2.csv");
        var guidelinePath = Path.Combine(root, "test-data", "category-label-guideline.md");
        var manifestPath = Path.Combine(root, "test-data", "transactions-phase2-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = document.RootElement;
        var rows = new TransactionCsvReader().Read(datasetPath, TransactionCsvPhase.Phase2);
        var rowIds = rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var developmentCount = 0;
        var heldOutCount = 0;

        Assert.Equal(
            manifest.GetProperty("dataset").GetProperty("sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(datasetPath))).ToLowerInvariant());
        Assert.Equal(
            manifest.GetProperty("guideline").GetProperty("sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(guidelinePath))).ToLowerInvariant());

        foreach (var group in manifest.GetProperty("groups").EnumerateObject())
        {
            var split = group.Value.GetProperty("split").GetString();
            var ids = group.Value.GetProperty("ids").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.Equal(5, ids.Length);
            Assert.All(ids, id => Assert.True(groupIds.Add(id), $"Duplicate group id {id}"));
            Assert.All(ids, id => Assert.Contains(id, rowIds));
            if (split == "development")
            {
                developmentCount += ids.Length;
            }
            else
            {
                Assert.Equal("held-out", split);
                heldOutCount += ids.Length;
            }
        }

        Assert.Equal(rowIds, groupIds);
        Assert.Equal(165, developmentCount);
        Assert.Equal(55, heldOutCount);
    }
}
