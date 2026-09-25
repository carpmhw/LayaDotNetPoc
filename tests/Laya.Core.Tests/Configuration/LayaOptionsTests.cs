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

    /// <summary>驗證未指定 CPU arena 時維持 ONNX Runtime 預設啟用行為。</summary>
    [Fact]
    public void Constructor_DefaultsCpuMemoryArenaToEnabled()
    {
        var options = new LayaOptions("relative-model");

        Assert.True(options.EnableCpuMemArena);
    }

    /// <summary>驗證呼叫端可明確停用 CPU memory arena。</summary>
    [Fact]
    public void Constructor_AllowsDisablingCpuMemoryArena()
    {
        var options = new LayaOptions("relative-model", enableCpuMemArena: false);

        Assert.False(options.EnableCpuMemArena);
    }
}
