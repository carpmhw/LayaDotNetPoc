using Laya.Core.Inference;

namespace Laya.Core.Tests.Inference;

[Trait("Category", "PureLogic")]
public sealed class LayaOnnxSessionOptionsTests
{
    /// <summary>驗證 session options 將 CPU arena 開啟狀態套用至 ONNX Runtime。</summary>
    [Fact]
    public void CreateSessionOptions_EnablesCpuArenaWhenRequested()
    {
        using var options = LayaOnnxSession.CreateSessionOptions(enableCpuMemArena: true);

        Assert.True(options.EnableCpuMemArena);
    }

    /// <summary>驗證 session options 將 CPU arena 關閉狀態套用至 ONNX Runtime。</summary>
    [Fact]
    public void CreateSessionOptions_DisablesCpuArenaWhenRequested()
    {
        using var options = LayaOnnxSession.CreateSessionOptions(enableCpuMemArena: false);

        Assert.False(options.EnableCpuMemArena);
    }
}
