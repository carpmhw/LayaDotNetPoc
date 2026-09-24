using Laya.Core.Configuration;
using Laya.Core.Exceptions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using TextEncoding = System.Text.Encoding;

namespace Laya.Core.Tests.Models;

public sealed class LayaModelValidatorTests
{
    /// <summary>驗證缺少模型資產時會回報缺失檔案。</summary>
    [Fact]
    public void ValidateBundle_ReportsMissingRequiredAsset()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            var exception = Assert.Throws<LayaModelNotFoundException>(
                () => LayaModelValidator.ValidateBundle(directory.FullName));

            Assert.Contains("laya.onnx", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證完整最小 bundle 可以讀取設定與必要路徑。</summary>
    [Fact]
    public void ValidateBundle_LoadsConfigAndPaths()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            CreateMinimalBundle(directory.FullName);

            var bundle = LayaModelValidator.ValidateBundle(directory.FullName);

            Assert.Equal(512, bundle.Config.MaxLength);
            Assert.Equal(192, bundle.Config.HeadMaxLength);
            Assert.Equal(Path.Combine(directory.FullName, "laya.onnx"), bundle.ModelPath);
            Assert.Equal(
                Path.Combine(directory.FullName, "tokenizer", "tokenizer.json"),
                bundle.TokenizerPath);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證無效模型設定不會以預設值靜默通過。</summary>
    [Fact]
    public void ValidateBundle_RejectsInvalidConfig()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            CreateMinimalBundle(directory.FullName);
            File.WriteAllText(
                Path.Combine(directory.FullName, "laya_config.json"),
                "{\"max_len\":0,\"head_max_len\":192,\"temperature\":[1.6,1.2,1.9]," +
                "\"temperature_by_options\":{\"choice:2\":1.9}}");

            Assert.Throws<LayaConfigurationException>(
                () => LayaModelValidator.ValidateBundle(directory.FullName));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證 manifest 會檢查每個資產的大小與 SHA-256。</summary>
    [Fact]
    public void ValidateBundle_ValidatesManifestHashes()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            CreateMinimalBundle(directory.FullName);
            var manifestPath = WriteManifest(directory.FullName, "english", "revision");

            var bundle = LayaModelValidator.ValidateBundle(
                directory.FullName,
                manifestPath,
                new LayaOptions(directory.FullName, "english", "revision"));

            Assert.Equal(manifestPath, bundle.ManifestPath);
            Assert.Equal("english", bundle.Profile);
            Assert.Equal("revision", bundle.CheckpointRevision);
            Assert.Equal(5, bundle.VerifiedFiles.Count);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證資產竄改會在模型初始化前失敗。</summary>
    [Fact]
    public void ValidateBundle_RejectsTamperedAsset()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            CreateMinimalBundle(directory.FullName);
            var manifestPath = WriteManifest(directory.FullName, "english", "revision");
            File.WriteAllText(Path.Combine(directory.FullName, "tokenizer", "tokenizer.json"), "[]");

            var exception = Assert.Throws<LayaConfigurationException>(() =>
                LayaModelValidator.ValidateBundle(directory.FullName, manifestPath));

            Assert.Contains("tokenizer/tokenizer.json", exception.Message, StringComparison.Ordinal);
            Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證 manifest 宣告的 external-data shard 缺失時不會補用其他檔案。</summary>
    [Fact]
    public void ValidateBundle_RejectsMissingExternalShard()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            CreateMinimalBundle(directory.FullName);
            var manifestPath = WriteManifest(directory.FullName, "english", "revision", "missing-shard.data");

            var exception = Assert.Throws<LayaModelNotFoundException>(() =>
                LayaModelValidator.ValidateBundle(directory.FullName, manifestPath));

            Assert.Contains("missing-shard.data", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>驗證 checkpoint 與 tokenizer provenance 不一致時會拒絕混用 bundle。</summary>
    [Fact]
    public void ValidateBundle_RejectsMismatchedTokenizerIdentity()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            CreateMinimalBundle(directory.FullName);
            var manifestPath = WriteManifest(directory.FullName, "english", "revision");
            var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = json.RootElement.Clone();
            var manifest = root.Deserialize<Dictionary<string, object?>>()!;
            manifest["tokenizerCheckpointRevision"] = "another-revision";
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

            var exception = Assert.Throws<LayaConfigurationException>(() =>
                LayaModelValidator.ValidateBundle(directory.FullName, manifestPath));

            Assert.Contains("tokenizer", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    /// <summary>建立供設定驗證使用的最小本機 bundle。</summary>
    private static void CreateMinimalBundle(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "tokenizer"));
        WriteGraph(root, "laya.onnx.data");
        File.WriteAllText(Path.Combine(root, "laya.onnx.data"), "weights");
        File.WriteAllText(
            Path.Combine(root, "laya_config.json"),
            "{" +
            "\"max_len\":512," +
            "\"head_max_len\":192," +
            "\"temperature\":[1.6,1.2,1.9]," +
            "\"temperature_by_options\":{\"choice:2\":1.9,\"noul:2\":1.9}" +
            "}");
        File.WriteAllText(Path.Combine(root, "tokenizer", "tokenizer.json"), "{}");
        File.WriteAllText(Path.Combine(root, "tokenizer", "tokenizer_config.json"), "{}");
    }

    /// <summary>建立含資產 hash 與 external shard 宣告的測試 manifest。</summary>
    private static string WriteManifest(
        string root,
        string profile,
        string revision,
        string? extraExternalShard = null)
    {
        var relativeFiles = new[]
        {
            "laya.onnx",
            "laya.onnx.data",
            "laya_config.json",
            "tokenizer/tokenizer.json",
            "tokenizer/tokenizer_config.json"
        };
        var files = relativeFiles.ToDictionary(
            path => path,
            path => new
            {
                size = new FileInfo(Path.Combine(root, path)).Length,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, path)))).ToLowerInvariant()
            },
            StringComparer.Ordinal);
        var manifest = new
        {
            profile,
            checkpointRevision = revision,
            tokenizerCheckpointRevision = revision,
            files,
            externalDataFiles = extraExternalShard is null
                ? new[] { "laya.onnx.data" }
                : new[] { "laya.onnx.data", extraExternalShard }
        };
        var path = Path.Combine(root, "laya-bundle-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        return path;
    }

    /// <summary>驗證 graph 真實引用決定零、一或多個 shard，並解析 v2 巢狀身分。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ValidateBundle_UsesActualGraphReferences(int count)
    {
        using var fixture = new BundleFixture(count);
        var bundle = LayaModelValidator.ValidateBundle(fixture.Root, null,
            new LayaOptions(fixture.Root, "multilingual", "revision"));
        Assert.Equal(count, bundle.ExternalDataPaths.Count);
        Assert.Equal("revision", bundle.CheckpointRevision);
        Assert.Equal(4 + count, bundle.VerifiedFiles.Count);
        if (count == 0)
            Assert.Null(bundle.ExternalDataPath);
    }

    /// <summary>驗證每個必要 graph、設定、tokenizer 與 shard 都不能漏列完整 metadata。</summary>
    [Theory]
    [InlineData("laya.onnx", "entry")]
    [InlineData("laya_config.json", "entry")]
    [InlineData("tokenizer/tokenizer.json", "entry")]
    [InlineData("tokenizer/tokenizer_config.json", "entry")]
    [InlineData("weights-0.bin", "entry")]
    [InlineData("weights-0.bin", "size")]
    [InlineData("weights-0.bin", "sha256")]
    public void ValidateBundle_RejectsIncompleteRequiredMetadata(string asset, string field)
    {
        using var fixture = new BundleFixture(1);
        var files = fixture.Manifest["files"]!.AsObject();
        if (field == "entry") files.Remove(asset);
        else files[asset]!.AsObject().Remove(field);
        fixture.Save();
        var exception = Assert.Throws<LayaConfigurationException>(() =>
            LayaModelValidator.ValidateBundle(fixture.Root));
        Assert.Contains(asset, exception.Message);
    }

    /// <summary>驗證 manifest 清單必須與 graph 引用集合完全一致。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateBundle_RejectsMissingOrExtraDeclaredShard(bool extra)
    {
        using var fixture = new BundleFixture(1);
        if (extra)
        {
            File.WriteAllText(Path.Combine(fixture.Root, "extra.bin"), "extra");
            fixture.Manifest["externalDataFiles"]!.AsArray().Add("extra.bin");
            fixture.AddFile("extra.bin");
        }
        else fixture.Manifest["externalDataFiles"] = new JsonArray();
        fixture.Save();
        var exception = Assert.Throws<LayaConfigurationException>(() =>
            LayaModelValidator.ValidateBundle(fixture.Root));
        Assert.Contains("external", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證 graph 引用的實體 shard 缺失時即使未列 manifest 仍失敗。</summary>
    [Fact]
    public void ValidateBundle_RejectsMissingGraphShard()
    {
        using var fixture = new BundleFixture(1);
        File.Delete(Path.Combine(fixture.Root, "weights-0.bin"));
        Assert.Throws<LayaModelNotFoundException>(() => LayaModelValidator.ValidateBundle(fixture.Root));
    }

    /// <summary>驗證 multilingual 強制使用 v2 巢狀身分且 options 無法繞過比對。</summary>
    [Theory]
    [InlineData("missing-manifest")]
    [InlineData("v1")]
    [InlineData("missing-profile")]
    [InlineData("missing-revision")]
    [InlineData("wrong-profile")]
    [InlineData("wrong-revision")]
    [InlineData("conflicting-revision")]
    [InlineData("wrong-root")]
    public void ValidateBundle_RejectsInvalidMultilingualIdentity(string failure)
    {
        using var fixture = new BundleFixture(1);
        switch (failure)
        {
            case "v1": fixture.Manifest["schemaVersion"] = 1; break;
            case "missing-profile": fixture.Manifest.Remove("profile"); break;
            case "missing-revision": fixture.Manifest["checkpoint"]!.AsObject().Remove("revision"); break;
            case "wrong-profile": fixture.Manifest["profile"] = "english"; break;
            case "wrong-revision": fixture.Manifest["checkpoint"]!["revision"] = "wrong"; break;
            case "conflicting-revision": fixture.Manifest["checkpointRevision"] = "wrong"; break;
        }
        fixture.Save();
        if (failure == "missing-manifest") File.Delete(fixture.ManifestPath);
        var optionsRoot = failure == "wrong-root" ? Path.Combine(fixture.Root, "other") : fixture.Root;
        Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(
            fixture.Root, null, new LayaOptions(optionsRoot, "multilingual", "revision")));
    }

    /// <summary>驗證 graph 及 manifest 的不安全路徑在任何平台都不能通過。</summary>
    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("/tmp/outside.bin")]
    [InlineData("C:/outside.bin")]
    [InlineData("..\\outside.bin")]
    [InlineData("weights/../outside.bin")]
    [InlineData("./weights-0.bin")]
    public void ValidateBundle_RejectsUnsafeGraphPath(string location)
    {
        using var fixture = new BundleFixture(1);
        WriteGraph(fixture.Root, location);
        fixture.AddFile("laya.onnx");
        fixture.Save();
        Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(fixture.Root));
    }

    /// <summary>驗證檔案或中間目錄符號連結不能使必要資產越出 root。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateBundle_RejectsSymlinkEscape(bool directoryLink)
    {
        using var fixture = new BundleFixture(1);
        using var outside = new BundleFixture(1);
        if (directoryLink)
        {
            Directory.Delete(Path.Combine(fixture.Root, "tokenizer"), true);
            Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "tokenizer"),
                Path.Combine(outside.Root, "tokenizer"));
        }
        else
        {
            File.Delete(Path.Combine(fixture.Root, "weights-0.bin"));
            File.CreateSymbolicLink(Path.Combine(fixture.Root, "weights-0.bin"),
                Path.Combine(outside.Root, "weights-0.bin"));
        }
        Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(fixture.Root));
    }

    /// <summary>驗證所有成功與失敗出口都關閉檔案，已回傳 bundle 只保存驗證快照。</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("graph")]
    [InlineData("config")]
    public void ValidateBundle_ReleasesFilesAndReturnsSnapshot(string outcome)
    {
        using var fixture = new BundleFixture(1);
        if (outcome == "hash") File.WriteAllText(Path.Combine(fixture.Root, "weights-0.bin"), "changed");
        if (outcome == "size") File.WriteAllText(Path.Combine(fixture.Root, "weights-0.bin"), "x");
        if (outcome == "graph") File.WriteAllBytes(Path.Combine(fixture.Root, "laya.onnx"), new byte[] { 58, 255 });
        if (outcome == "config")
        {
            File.WriteAllText(Path.Combine(fixture.Root, "laya_config.json"), "{}");
            fixture.AddFile("laya_config.json");
            fixture.Save();
        }
        if (outcome == "success")
        {
            var bundle = LayaModelValidator.ValidateBundle(fixture.Root);
            File.WriteAllText(Path.Combine(fixture.Root, "weights-0.bin"), "modified after initialization");
            Assert.Equal(5, bundle.VerifiedFiles.Count);
            Assert.Single(bundle.ExternalDataPaths);
        }
        else Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(fixture.Root));
        foreach (var path in Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories))
        {
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    /// <summary>驗證明確 manifest 路徑不能透過跨 root 或符號連結繞過 bundle 邊界。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateBundle_RejectsEscapingExplicitManifest(bool symbolicLink)
    {
        using var fixture = new BundleFixture(1);
        using var outside = new BundleFixture(1);
        var path = outside.ManifestPath;
        if (symbolicLink)
        {
            File.Delete(fixture.ManifestPath);
            File.CreateSymbolicLink(fixture.ManifestPath, outside.ManifestPath);
            path = fixture.ManifestPath;
        }
        Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(fixture.Root, path));
    }

    /// <summary>驗證 profile 與 checkpoint repository 不可混用。</summary>
    [Fact]
    public void ValidateBundle_RejectsWrongMultilingualRepository()
    {
        using var fixture = new BundleFixture(1);
        fixture.Manifest["checkpoint"]!["repository"] = "english/checkpoint";
        fixture.Save();
        Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(fixture.Root));
    }

    /// <summary>驗證 runtime 不會直接載入尚未 publication 的 staging candidate。</summary>
    [Fact]
    public void ValidateBundle_RejectsStagedMultilingualCandidate()
    {
        using var fixture = new BundleFixture(1);
        fixture.Manifest["status"] = "candidate-staged";
        fixture.Save();

        var error = Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(
            fixture.Root,
            null,
            new LayaOptions(fixture.Root, "multilingual", "revision")));

        Assert.Contains("verified", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證相同 JSON key 的多份身分或 metadata 不能以最後一份覆蓋。</summary>
    [Theory]
    [InlineData("profile", "\"english\"")]
    [InlineData("files", "{}")]
    public void ValidateBundle_RejectsDuplicateManifestKeys(string key, string value)
    {
        using var fixture = new BundleFixture(1);
        var json = File.ReadAllText(fixture.ManifestPath);
        File.WriteAllText(fixture.ManifestPath, $"{{\"{key}\":{value}," + json[1..]);
        Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(fixture.Root));
    }

    /// <summary>以環境變數明確加入本機真 bundle，未指定時仍執行合成串流測試。</summary>
    public static IEnumerable<object?[]> StreamingBundles()
    {
        yield return new object?[] { null, "multilingual", "revision" };
        var multilingual = Environment.GetEnvironmentVariable("LAYA_VALIDATOR_MULTILINGUAL_ROOT");
        if (!string.IsNullOrWhiteSpace(multilingual))
            yield return new object?[] { multilingual, "multilingual", "052592a15d198d9ad47da779604259b10b47b7aa" };
        var english = Environment.GetEnvironmentVariable("LAYA_VALIDATOR_ENGLISH_ROOT");
        if (!string.IsNullOrWhiteSpace(english))
            yield return new object?[] { english, "english", null };
    }

    /// <summary>驗證完整 bundle 串流 hash 的記憶體成本不隨大 shard 成長。</summary>
    [Theory]
    [MemberData(nameof(StreamingBundles))]
    public void ValidateBundle_StreamsHashesForSyntheticAndConfiguredBundles(string? root, string profile, string? revision)
    {
        using var fixture = root is null ? new BundleFixture(1) : null;
        if (fixture is not null)
        {
            using (var stream = File.OpenWrite(Path.Combine(fixture.Root, "weights-0.bin")))
                stream.SetLength(32 * 1024 * 1024);
            fixture.AddFile("weights-0.bin");
            fixture.Save();
        }
        root ??= fixture!.Root;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var bundle = LayaModelValidator.ValidateBundle(root, null, new LayaOptions(root, profile, revision));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 16 * 1024 * 1024, $"Validation allocated {allocated} bytes for '{root}'.");
        Assert.NotEmpty(bundle.ExternalDataPaths);
        if (profile == "multilingual")
        {
            Assert.Equal(profile, bundle.Profile);
            Assert.Equal(revision, bundle.CheckpointRevision);
            Assert.Equal(5, bundle.VerifiedFiles.Count);
        }
    }

    /// <summary>建立只有必要 protobuf 欄位的 graph，避免以任意字串偽裝 ONNX。</summary>
    private static void WriteGraph(string root, params string[] locations)
    {
        File.WriteAllBytes(Path.Combine(root, "laya.onnx"),
            ProtoField(7, locations.SelectMany(location => ProtoField(5, ExternalTensor(location))).ToArray()));
    }

    /// <summary>編碼帶有 location 與 EXTERNAL enum 的 tensor。</summary>
    internal static byte[] ExternalTensor(string location)
    {
        return ProtoField(13, ProtoField(1, TextEncoding.UTF8.GetBytes("location"))
            .Concat(ProtoField(2, TextEncoding.UTF8.GetBytes(location))).ToArray())
            .Concat(new byte[] { 112, 1 }).ToArray();
    }

    /// <summary>以獨立 protobuf 編碼器建立測試訊息欄位。</summary>
    internal static byte[] ProtoField(int number, byte[] content)
    {
        using var stream = new MemoryStream();
        using (var output = new Google.Protobuf.CodedOutputStream(stream, leaveOpen: true))
        {
            output.WriteTag(number, Google.Protobuf.WireFormat.WireType.LengthDelimited);
            output.WriteBytes(Google.Protobuf.ByteString.CopyFrom(content));
        }
        return stream.ToArray();
    }

    /// <summary>建立與清理每個案例獨立的 v2 bundle。</summary>
    internal sealed class BundleFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory().FullName;
        public string ManifestPath => Path.Combine(Root, "laya-bundle-manifest.json");
        public JsonObject Manifest { get; }

        /// <summary>建立可調整 shard 數量的完整 v2 manifest。</summary>
        public BundleFixture(int shards)
        {
            CreateMinimalBundle(Root);
            File.Delete(Path.Combine(Root, "laya.onnx.data"));
            var locations = Enumerable.Range(0, shards).Select(index => $"weights-{index}.bin").ToArray();
            WriteGraph(Root, locations);
            foreach (var location in locations) File.WriteAllText(Path.Combine(Root, location), "weights");
            Manifest = new JsonObject
            {
                ["schemaVersion"] = 2,
                ["status"] = "verified",
                ["profile"] = "multilingual",
                ["checkpoint"] = new JsonObject { ["repository"] = "convaiinnovations/laya-multilingual", ["revision"] = "revision" },
                ["files"] = new JsonObject(),
                ["externalDataFiles"] = new JsonArray(locations.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
            };
            foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                AddFile(Path.GetRelativePath(Root, path).Replace('\\', '/'));
            Save();
        }

        /// <summary>以串流產生測試資產 metadata。</summary>
        public void AddFile(string relativePath)
        {
            using var stream = File.OpenRead(Path.Combine(Root, relativePath));
            Manifest["files"]![relativePath] = new JsonObject
            {
                ["size"] = stream.Length,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
            };
        }

        /// <summary>保存調整後的 manifest。</summary>
        public void Save() => File.WriteAllText(ManifestPath, Manifest.ToJsonString());

        /// <summary>清除案例的暫存資產。</summary>
        public void Dispose() => Directory.Delete(Root, true);
    }
}
