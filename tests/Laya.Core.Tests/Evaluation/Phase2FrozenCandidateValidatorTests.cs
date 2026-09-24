using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Laya.ConsoleApp.Evaluation;
using Laya.Core.Evaluation;
using Laya.Core.Exceptions;
using Laya.Shared.Configuration;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class Phase2FrozenCandidateValidatorTests
{
    /// <summary>驗證完整 candidate 可引用 development artifact 並固定 held-out policy。</summary>
    [Fact]
    public void Validate_CompleteCandidate_ReturnsFixedPolicyAndArtifactHash()
    {
        using var fixture = new CandidateFixture();
        var candidate = fixture.CreateCandidate();

        var validated = new Phase2FrozenCandidateValidator().Validate(
            candidate,
            fixture.Root,
            fixture.Profile,
            fixture.Development,
            fixture.HeldOut,
            fixture.ReferencePath,
            LayaPromptVariant.A,
            new DateTimeOffset(2026, 9, 24, 1, 0, 0, TimeSpan.Zero));

        Assert.Equal("Balanced", validated.Policy.Name);
        Assert.Equal(fixture.HeldOut.SelectedIdsHash, validated.HeldOutSelectedIdsHash);
        Assert.Equal(Hash(File.ReadAllBytes(candidate)), validated.ManifestSha256);
    }

    /// <summary>計算測試 candidate artifact 的小寫 SHA-256。</summary>
    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>驗證 policy hash 被竄改時在任何 inference 前拒絕 candidate。</summary>
    [Fact]
    public void Validate_TamperedPolicyHash_IsRejected()
    {
        using var fixture = new CandidateFixture();
        var candidatePath = fixture.CreateCandidate();
        var json = JsonNode.Parse(File.ReadAllText(candidatePath))!;
        json["policyHash"] = new string('f', 64);
        File.WriteAllText(candidatePath, json.ToJsonString());

        var error = Assert.Throws<LayaConfigurationException>(() => new Phase2FrozenCandidateValidator().Validate(
            candidatePath,
            fixture.Root,
            fixture.Profile,
            fixture.Development,
            fixture.HeldOut,
            fixture.ReferencePath,
            LayaPromptVariant.A,
            new DateTimeOffset(2026, 9, 24, 1, 0, 0, TimeSpan.Zero)));

        Assert.Contains("policy hash", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證不一致的 held-out selected IDs 不能被 candidate 自己宣告繞過。</summary>
    [Fact]
    public void Validate_MismatchedHeldOutSelection_IsRejected()
    {
        using var fixture = new CandidateFixture();
        var candidatePath = fixture.CreateCandidate();
        var json = JsonNode.Parse(File.ReadAllText(candidatePath))!;
        json["dataset"]!["heldOutSelectedIdsHash"] = new string('f', 64);
        File.WriteAllText(candidatePath, json.ToJsonString());

        var error = Assert.Throws<LayaConfigurationException>(() => new Phase2FrozenCandidateValidator().Validate(
            candidatePath,
            fixture.Root,
            fixture.Profile,
            fixture.Development,
            fixture.HeldOut,
            fixture.ReferencePath,
            LayaPromptVariant.A,
            new DateTimeOffset(2026, 9, 24, 1, 0, 0, TimeSpan.Zero)));

        Assert.Contains("dataset or split identity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>以暫存檔案建立不依賴模型權重的 candidate validation fixture。</summary>
    private sealed class CandidateFixture : IDisposable
    {
        public CandidateFixture()
        {
            Root = Directory.CreateTempSubdirectory("laya-frozen-candidate-").FullName;
            ModelRoot = Directory.CreateDirectory(Path.Combine(Root, "models")).FullName;
            Directory.CreateDirectory(Path.Combine(Root, "reports", "dev"));
            Directory.CreateDirectory(Path.Combine(Root, "test-data"));
            File.WriteAllText(Path.Combine(ModelRoot, "laya-bundle-manifest.json"), "{\"schemaVersion\":2}");
            ReferencePath = Path.Combine(Root, "test-data", "reference.json");
            File.WriteAllText(ReferencePath, "{\"status\":\"complete\"}");
            Development = CreateSelection("development", "1");
            HeldOut = CreateSelection("held-out", "2");
            Profile = new LayaProfileResolution(
                "multilingual",
                ModelRoot,
                "052592a15d198d9ad47da779604259b10b47b7aa",
                "test");
        }

        public string Root { get; }

        public string ModelRoot { get; }

        public string ReferencePath { get; }

        public LayaProfileResolution Profile { get; }

        public Phase2DatasetSelection Development { get; }

        public Phase2DatasetSelection HeldOut { get; }

        /// <summary>產生合法 candidate 及其 development manifest/results artifact。</summary>
        public string CreateCandidate()
        {
            var createdUtc = "2026-09-23T23:00:00Z";
            var policy = new LayaPhase2Policy("Balanced", true, 0.95, 0.2, 0.6, 0.1);
            var manifest = new
            {
                schemaVersion = 2,
                runId = "development-run",
                profile = "multilingual",
                modelRevision = Profile.CheckpointRevision,
                datasetHash = Development.DatasetHash,
                guidelineHash = Development.GuidelineHash,
                datasetManifestHash = Development.ManifestHash,
                split = "development",
                promptVariant = "A",
                promptHash = LayaPhase2Contracts.PromptHash(LayaPromptVariant.A),
                selectedIdsHash = Development.SelectedIdsHash,
                selectedIds = Development.SelectedIds,
                policyHash = LayaPhase2Contracts.PolicyHash(policy),
                createdUtc
            };
            var manifestPath = Path.Combine(Root, "reports", "dev", "manifest.json");
            var resultsPath = Path.Combine(Root, "reports", "dev", "results.json");
            var manifestJson = JsonSerializer.Serialize(manifest);
            File.WriteAllText(manifestPath, manifestJson);
            File.WriteAllText(resultsPath, JsonSerializer.Serialize(new
            {
                complete = true,
                inputCount = 1,
                manifest
            }));

            var candidate = new
            {
                schemaVersion = 1,
                kind = "laya.phase2.frozen-candidate",
                frozenAtUtc = "2026-09-24T00:00:00Z",
                developmentRun = new
                {
                    runId = "development-run",
                    manifestPath = "reports/dev/manifest.json",
                    manifestSha256 = Hash(File.ReadAllBytes(manifestPath)),
                    resultsPath = "reports/dev/results.json",
                    resultsSha256 = Hash(File.ReadAllBytes(resultsPath))
                },
                model = new
                {
                    profile = Profile.Name,
                    checkpointRevision = Profile.CheckpointRevision,
                    bundleManifestSha256 = Hash(File.ReadAllBytes(Path.Combine(ModelRoot, "laya-bundle-manifest.json")))
                },
                reference = new
                {
                    manifestPath = "test-data/reference.json",
                    manifestSha256 = Hash(File.ReadAllBytes(ReferencePath))
                },
                dataset = new
                {
                    manifestSha256 = Development.ManifestHash,
                    datasetSha256 = Development.DatasetHash,
                    guidelineSha256 = Development.GuidelineHash,
                    sourceSplit = "development",
                    targetSplit = "held-out",
                    developmentSelectedIdsHash = Development.SelectedIdsHash,
                    heldOutSelectedIdsHash = HeldOut.SelectedIdsHash
                },
                request = new
                {
                    promptVariant = "A",
                    promptHash = LayaPhase2Contracts.PromptHash(LayaPromptVariant.A),
                    optionOrderHash = LayaPhase2Contracts.OptionOrderHash(),
                    serializationVersion = LayaPhase2Contracts.SerializationVersion,
                    serializationHash = LayaPhase2Contracts.SerializationHash()
                },
                policy = new
                {
                    name = policy.Name,
                    autoEnabled = policy.AutoEnabled,
                    autoProbabilityThreshold = policy.AutoProbabilityThreshold,
                    autoMarginThreshold = policy.AutoMarginThreshold,
                    suggestProbabilityThreshold = policy.SuggestProbabilityThreshold,
                    suggestMarginThreshold = policy.SuggestMarginThreshold
                },
                policyHash = LayaPhase2Contracts.PolicyHash(policy)
            };
            var path = Path.Combine(Root, "candidate.json");
            File.WriteAllText(path, JsonSerializer.Serialize(candidate));
            return path;
        }

        /// <summary>釋放暫存 candidate 目錄。</summary>
        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }

        /// <summary>建立含固定 hash 的 selection，不依賴 Phase 2 大型 CSV。</summary>
        private static Phase2DatasetSelection CreateSelection(string split, string id)
        {
            var selectedIds = new[] { id };
            return new Phase2DatasetSelection(
                split,
                Array.Empty<TransactionRow>(),
                selectedIds,
                Hash(JsonSerializer.SerializeToUtf8Bytes(selectedIds)),
                new string('a', 64),
                new string('b', 64),
                new string('c', 64));
        }

        /// <summary>計算暫存 artifact 的小寫 SHA-256。</summary>
        private static string Hash(byte[] bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }
}
