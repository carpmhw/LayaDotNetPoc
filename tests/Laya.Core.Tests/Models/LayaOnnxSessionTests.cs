using Laya.Core.Inference;

namespace Laya.Core.Tests.Models;

[Trait("Category", "EnglishModel")]
public sealed class LayaOnnxSessionTests
{
    /// <summary>驗證實際 English bundle 可建立 CPU session 並暴露必要 metadata。</summary>
    [Fact]
    public void Open_ValidatesEnglishBundleSchema()
    {
        var modelRoot = FindModelRoot();

        using var session = LayaOnnxSession.Open(modelRoot);

        Assert.Contains("input_ids", session.InputMetadata.Keys);
        Assert.Contains("attention_mask", session.InputMetadata.Keys);
        Assert.Contains("marker_pos", session.InputMetadata.Keys);
        Assert.Contains("marker_mask", session.InputMetadata.Keys);
        Assert.Contains("qtype", session.InputMetadata.Keys);
        Assert.Contains("logits", session.OutputMetadata.Keys);
        Assert.Contains("act_probs", session.OutputMetadata.Keys);
        Assert.True(session.LoadDuration > TimeSpan.Zero);

        AssertTensor(session.InputMetadata["input_ids"], typeof(long), -1, -1);
        AssertTensor(session.InputMetadata["attention_mask"], typeof(long), -1, -1);
        AssertTensor(session.InputMetadata["marker_pos"], typeof(long), -1, -1);
        AssertTensor(session.InputMetadata["marker_mask"], typeof(bool), -1, -1);
        AssertTensor(session.InputMetadata["qtype"], typeof(long), -1);
        AssertTensor(session.OutputMetadata["logits"], typeof(float), -1, -1);
        AssertTensor(session.OutputMetadata["act_probs"], typeof(float), -1, 2);
    }

    /// <summary>尋找 repository 內的本機模型目錄，允許 CI 透過環境變數覆寫。</summary>
    private static string FindModelRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LAYA_MODEL_ROOT");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../models/laya"));
    }

    /// <summary>驗證 metadata 的 CLR dtype 與 rank 契約。</summary>
    private static void AssertTensor(
        Laya.Core.Inference.LayaTensorMetadata metadata,
        Type elementType,
        params long[] dimensions)
    {
        Assert.Equal(elementType, metadata.ElementType);
        Assert.Equal(dimensions, metadata.Dimensions);
    }
}
