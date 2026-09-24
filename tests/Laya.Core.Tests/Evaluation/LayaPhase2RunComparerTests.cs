using Laya.Core.Evaluation;
using Laya.Core.Exceptions;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class LayaPhase2RunComparerTests
{
    /// <summary>驗證不同 model/reference 可在相同輸入契約下產生共同成功集合。</summary>
    [Fact]
    public void Compare_AllowsDifferentModelsAndKeepsCommonSuccesses()
    {
        var left = CreateManifest("english", "model-a");
        var right = CreateManifest("multilingual", "model-b");

        var comparison = LayaPhase2RunComparer.Compare(left, ["id-1", "id-2"], right, ["id-2"]);

        Assert.Equal(2, comparison.LeftInputCount);
        Assert.Equal(2, comparison.RightInputCount);
        Assert.Equal(new[] { "id-2" }, comparison.CommonSuccessfulIds);
    }

    /// <summary>驗證 dataset、prompt 或 runtime mismatch 不能產生可比較結論。</summary>
    [Theory]
    [InlineData("dataset")]
    [InlineData("prompt")]
    [InlineData("environment")]
    public void Compare_MismatchedContract_IsRejected(string mismatch)
    {
        var left = CreateManifest("english", "model-a");
        var right = CreateManifest("multilingual", "model-b") with
        {
            DatasetHash = mismatch == "dataset" ? "different-dataset" : left.DatasetHash,
            PromptHash = mismatch == "prompt" ? "different-prompt" : left.PromptHash,
            EnvironmentFingerprint = mismatch == "environment" ? "different-environment" : left.EnvironmentFingerprint
        };

        var error = Assert.Throws<LayaConfigurationException>(() =>
            LayaPhase2RunComparer.Compare(left, ["id-1"], right, ["id-1"]));

        Assert.Contains("mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>驗證成功集合含未選取 ID 時拒絕，避免錯誤分母被拼接。</summary>
    [Fact]
    public void Compare_SuccessOutsideSelectedIds_IsRejected()
    {
        var left = CreateManifest("english", "model-a");
        var right = CreateManifest("multilingual", "model-b");

        Assert.Throws<LayaConfigurationException>(() =>
            LayaPhase2RunComparer.Compare(left, ["outside"], right, ["id-1"]));
    }

    /// <summary>建立具有完整 v2 comparison provenance 的合成 manifest。</summary>
    private static LayaPhase2RunManifest CreateManifest(string profile, string model)
    {
        var selectedIds = new[] { "id-1", "id-2" };
        return new LayaPhase2RunManifest(
            $"run-{profile}",
            profile,
            model,
            "dataset",
            "development",
            "A",
            LayaPhase2Contracts.PromptHash(LayaPromptVariant.A),
            null,
            DateTimeOffset.UnixEpoch,
            "synthetic",
            "guideline",
            "dataset-manifest",
            LayaPhase2Contracts.Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(selectedIds)),
            "bundle",
            "reference",
            LayaPhase2Contracts.OptionOrderHash(),
            LayaPhase2Contracts.SerializationVersion,
            LayaPhase2Contracts.SerializationHash(),
            null,
            null,
            selectedIds,
            "fixture",
            "source",
            false,
            "source-digest",
            "environment",
            8);
    }
}
