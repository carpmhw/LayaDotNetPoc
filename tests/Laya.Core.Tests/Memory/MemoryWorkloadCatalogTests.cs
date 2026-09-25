extern alias MemoryProbe;

using Laya.Core.Models;
using MemoryProbe::Laya.MemoryProbe.Scenarios;

namespace Laya.Core.Tests.Memory;

[Trait("Category", "PureLogic")]
public sealed class MemoryWorkloadCatalogTests
{
    /// <summary>驗證 short／medium／long fixture 保持四欄 state 並建立 1／2／5 題。</summary>
    [Theory]
    [InlineData("short", 1)]
    [InlineData("medium", 2)]
    [InlineData("long", 5)]
    public void CreateRequest_PreservesFourFieldsAndQuestionCount(string stateProfile, int questionCount)
    {
        using var catalog = LoadCatalog();

        var request = catalog.CreateRequest(stateProfile, questionCount);

        var state = Assert.IsType<Dictionary<string, object?>>(request.State);
        Assert.Equal(new[] { "description", "amount", "currency", "direction" }, state.Keys);
        Assert.Equal(questionCount, request.Questions.Count);
        Assert.Contains("超市採購日用品", Assert.IsType<string>(state["description"]), StringComparison.Ordinal);
        Assert.Equal(LayaQuestionType.Choice, request.Questions[0].Type);
        Assert.Equal("food", request.Questions[0].Options[0]);
    }

    /// <summary>驗證 tokenizer fixture 含有固定英文、中文與混合文字。</summary>
    [Fact]
    public void GetTokenizerInputs_ReturnsAllThreeLanguages()
    {
        using var catalog = LoadCatalog();

        var inputs = catalog.GetTokenizerInputs();

        Assert.Equal(new[] { "en", "zh", "mixed" }, inputs.Select(input => input.Language));
        Assert.All(inputs, input => Assert.False(string.IsNullOrWhiteSpace(input.Text)));
    }

    /// <summary>驗證 calibration fixture 沿用固定 reference request 與 raw output shape。</summary>
    [Fact]
    public void CalibrationFixture_PreservesReferenceQuestionsAndRawValues()
    {
        using var catalog = LoadCatalog();

        var request = catalog.CreateCalibrationRequest();
        var raw = catalog.CreateCalibrationResult();

        Assert.Equal(2, request.Questions.Count);
        Assert.Equal(new[] { "food", "other" }, request.Questions[0].Options);
        Assert.Equal(LayaQuestionType.Noul, request.Questions[1].Type);
        Assert.Equal(4, raw.Logits.Length);
        Assert.Equal(new[] { 1f, 0f, 1f, 0f }, raw.ActProbabilities);
    }

    /// <summary>驗證 integrity workload 為唯一 ID 且每個 state 維持四欄。</summary>
    [Fact]
    public void GetIntegrityRequests_ReturnsDistinctFourFieldInputs()
    {
        using var catalog = LoadCatalog();

        var requests = catalog.GetIntegrityRequests();

        Assert.Equal(4, requests.Count);
        Assert.Equal(4, requests.Select(request => request.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(requests, request =>
        {
            var state = Assert.IsType<Dictionary<string, object?>>(request.Request.State);
            Assert.Equal(new[] { "description", "amount", "currency", "direction" }, state.Keys);
            Assert.Equal(2, request.Request.Questions.Count);
        });
    }

    /// <summary>驗證 workload fixture hash 不符時 catalog 拒絕讀取。</summary>
    [Fact]
    public void Load_RejectsWorkloadHashMismatch()
    {
        var fixturePath = GetFixturePath();
        var manifestPath = GetManifestPath();
        var tamperedPath = Path.Combine(Path.GetTempPath(), $"phase3a-{Guid.NewGuid():N}.json");
        File.Copy(fixturePath, tamperedPath);
        File.AppendAllText(tamperedPath, " ");

        try
        {
            Assert.Throws<InvalidDataException>(() => MemoryWorkloadCatalog.Load(tamperedPath, manifestPath));
        }
        finally
        {
            File.Delete(tamperedPath);
        }
    }

    /// <summary>載入 repository 內固定 Phase 3A workload fixture。</summary>
    private static MemoryWorkloadCatalog LoadCatalog()
    {
        return MemoryWorkloadCatalog.Load(GetFixturePath(), GetManifestPath());
    }

    /// <summary>取得 workload fixture 的 repository 絕對路徑。</summary>
    private static string GetFixturePath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../test-data/phase3a/memory-workloads.json"));
    }

    /// <summary>取得 workload manifest 的 repository 絕對路徑。</summary>
    private static string GetManifestPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../test-data/phase3a/memory-workloads-manifest.json"));
    }
}
