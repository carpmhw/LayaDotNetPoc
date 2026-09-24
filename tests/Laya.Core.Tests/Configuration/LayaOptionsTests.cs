using Laya.Core.Configuration;

namespace Laya.Core.Tests.Configuration;

[Trait("Category", "PureLogic")]
public sealed class LayaOptionsTests
{
    /// <summary>驗證 Core options 只接受 host 已解析的 root 並保存模型身分。</summary>
    [Fact]
    public void Constructor_StoresResolvedRootAndIdentity()
    {
        var options = new LayaOptions(
            "relative-model",
            "multilingual",
            "checkpoint-revision",
            "manifest.json");

        Assert.Equal(Path.GetFullPath("relative-model"), options.ModelRoot);
        Assert.Equal("multilingual", options.Profile);
        Assert.Equal("checkpoint-revision", options.CheckpointRevision);
        Assert.Equal(Path.GetFullPath("manifest.json"), options.ManifestPath);
    }
}
