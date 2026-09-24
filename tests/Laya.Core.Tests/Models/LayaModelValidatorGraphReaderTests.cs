using Laya.Core.Configuration;
using Laya.Core.Exceptions;
using static Laya.Core.Tests.Models.LayaModelValidatorTests;

namespace Laya.Core.Tests.Models;

/// <summary>透過公開 validator 驗證 protobuf reader，而不新增測試專用公開 API。</summary>
public sealed class LayaModelValidatorGraphReaderTests
{
    /// <summary>驗證 tensor、重複 tensor、巢狀 graph、稀疏 tensor 與 function 的引用。</summary>
    [Theory]
    [InlineData("tensor")]
    [InlineData("tensors")]
    [InlineData("graph")]
    [InlineData("graphs")]
    [InlineData("sparse")]
    [InlineData("sparse-attribute")]
    [InlineData("sparse-attributes")]
    [InlineData("function")]
    [InlineData("function-default")]
    [InlineData("training")]
    public void Reader_FindsNestedReferences(string kind)
    {
        using var fixture = new BundleFixture(1);
        var tensor = ExternalTensor("weights-0.bin");
        var graph = ProtoField(5, tensor);
        var attribute = kind switch
        {
            "tensors" => ProtoField(10, tensor),
            "graph" => ProtoField(6, graph),
            "graphs" => ProtoField(11, graph),
            "sparse-attribute" => ProtoField(22, ProtoField(1, tensor)),
            "sparse-attributes" => ProtoField(23, ProtoField(2, tensor)),
            _ => ProtoField(5, tensor)
        };
        var node = ProtoField(5, attribute);
        var model = kind switch
        {
            "sparse" => ProtoField(7, ProtoField(15, ProtoField(1, tensor).Concat(ProtoField(2, tensor)).ToArray())),
            "function" => ProtoField(7, Array.Empty<byte>()).Concat(ProtoField(25, ProtoField(7, node))).ToArray(),
            "function-default" => ProtoField(7, Array.Empty<byte>()).Concat(ProtoField(25, ProtoField(11, attribute))).ToArray(),
            "training" => ProtoField(7, Array.Empty<byte>()).Concat(ProtoField(20, ProtoField(1, graph))).ToArray(),
            _ => ProtoField(7, ProtoField(1, node))
        };
        File.WriteAllBytes(Path.Combine(fixture.Root, "laya.onnx"), model);
        fixture.AddFile("laya.onnx");
        fixture.Save();
        Assert.Single(LayaModelValidator.ValidateBundle(fixture.Root).ExternalDataPaths);
        fixture.Manifest["externalDataFiles"] = new System.Text.Json.Nodes.JsonArray();
        fixture.Save();
        Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(fixture.Root));
    }

    /// <summary>驗證 raw_data 與文字中的 location 不被誤認為 graph 引用。</summary>
    [Fact]
    public void Reader_IgnoresExternalLookingRawDataAndStrings()
    {
        using var fixture = new BundleFixture(0);
        var misleading = ExternalTensor("not-a-real-shard.bin");
        var model = ProtoField(7, ProtoField(5, ProtoField(9, misleading)))
            .Concat(ProtoField(6, misleading)).ToArray();
        File.WriteAllBytes(Path.Combine(fixture.Root, "laya.onnx"), model);
        fixture.AddFile("laya.onnx");
        fixture.Save();
        Assert.Empty(LayaModelValidator.ValidateBundle(fixture.Root).ExternalDataPaths);
    }

    /// <summary>驗證 malformed wire、缺 location、錯誤 enum 與越過父訊息邊界均被拒絕。</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("truncated")]
    [InlineData("boundary")]
    [InlineData("zero-tag")]
    [InlineData("wrong-wire")]
    [InlineData("varint-overflow")]
    [InlineData("missing-location")]
    [InlineData("empty-location")]
    [InlineData("duplicate-location")]
    [InlineData("missing-external-enum")]
    [InlineData("depth")]
    public void Reader_RejectsMalformedGraph(string kind)
    {
        using var fixture = new BundleFixture(0);
        var tensor = ExternalTensor("unused.bin");
        var graph = ProtoField(5, tensor);
        if (kind == "depth")
            for (var i = 0; i < 100; i++) graph = ProtoField(1, ProtoField(5, ProtoField(6, graph)));
        var model = kind switch
        {
            "empty" => Array.Empty<byte>(),
            "truncated" => new byte[] { 58, 2, 42 },
            "boundary" => new byte[] { 58, 2, 42, 2, 0, 0 },
            "zero-tag" => new byte[] { 58, 1, 0 },
            "wrong-wire" => new byte[] { 56, 0 },
            "varint-overflow" => Enumerable.Repeat((byte)255, 11).ToArray(),
            "missing-location" => ProtoField(7, ProtoField(5, new byte[] { 112, 1 })),
            "empty-location" => ProtoField(7, ProtoField(5, ExternalTensor(""))),
            "duplicate-location" => ProtoField(7, ProtoField(5, tensor.Concat(tensor).ToArray())),
            "missing-external-enum" => ProtoField(7, ProtoField(5, tensor[..^2])),
            _ => ProtoField(7, graph)
        };
        File.WriteAllBytes(Path.Combine(fixture.Root, "laya.onnx"), model);
        fixture.AddFile("laya.onnx");
        fixture.Save();
        var error = Assert.Throws<LayaConfigurationException>(() => LayaModelValidator.ValidateBundle(fixture.Root));
        Assert.Contains("ONNX", error.Message);
        using var exclusive = new FileStream(Path.Combine(fixture.Root, "laya.onnx"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>驗證一 GiB 的稀疏 raw_data 不會配置同等大小記憶體，並仍能解析其後的 shard。</summary>
    [Fact]
    public void Reader_SkipsGigabyteRawDataWithBoundedMemory()
    {
        using var fixture = new BundleFixture(1);
        File.Delete(fixture.ManifestPath);
        var path = Path.Combine(fixture.Root, "laya.onnx");
        const int rawLength = 1024 * 1024 * 1024;
        var following = ProtoField(5, ExternalTensor("weights-0.bin"));
        // 長度前綴由獨立 Google.Protobuf encoder 產生，raw_data 留為稀疏檔案區段。
        using (var stream = File.Create(path))
        {
            using (var output = new Google.Protobuf.CodedOutputStream(stream, leaveOpen: true))
            {
                output.WriteTag(7, Google.Protobuf.WireFormat.WireType.LengthDelimited);
                output.WriteLength(rawLength + 12 + following.Length);
                output.WriteTag(5, Google.Protobuf.WireFormat.WireType.LengthDelimited);
                output.WriteLength(rawLength + 6);
                output.WriteTag(9, Google.Protobuf.WireFormat.WireType.LengthDelimited);
                output.WriteLength(rawLength);
            }
            stream.Seek(rawLength, SeekOrigin.Current);
            stream.Write(following);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var bundle = LayaModelValidator.ValidateBundle(fixture.Root);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Single(bundle.ExternalDataPaths);
        Assert.True(allocated < 8 * 1024 * 1024, $"Reader allocated {allocated} bytes.");
    }
}
