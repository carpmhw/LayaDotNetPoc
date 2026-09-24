using Laya.Core.Exceptions;
using Laya.Core.PostProcessing;

namespace Laya.Core.Tests.PostProcessing;

[Trait("Category", "PureLogic")]
public sealed class LayaCalibrationTests
{
    /// <summary>驗證 stable softmax 的分布總和與基本大小關係。</summary>
    [Fact]
    public void Softmax_ReturnsNormalizedDistribution()
    {
        var probabilities = LayaCalibration.Softmax(new[] { 1f, 2f, 3f }, 1.5);

        Assert.Equal(1d, probabilities.Sum(), precision: 12);
        Assert.True(probabilities[2] > probabilities[1]);
        Assert.True(probabilities[1] > probabilities[0]);
    }

    /// <summary>驗證相同 logits 會產生平均機率。</summary>
    [Fact]
    public void Softmax_EqualLogitsAreUniform()
    {
        var probabilities = LayaCalibration.Softmax(new[] { 2f, 2f, 2f }, 1.5);

        Assert.All(probabilities, probability => Assert.Equal(1d / 3d, probability, precision: 12));
    }

    /// <summary>驗證極端但有限的 logits 不會造成 overflow 或 NaN。</summary>
    [Fact]
    public void Softmax_IsStableForExtremeFiniteLogits()
    {
        var probabilities = LayaCalibration.Softmax(new[] { -1000f, 1000f }, 1);

        Assert.Equal(0d, probabilities[0], precision: 12);
        Assert.Equal(1d, probabilities[1], precision: 12);
    }

    /// <summary>驗證有效 logits 與 temperature 的非法值會被拒絕。</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Softmax_RejectsInvalidTemperature(double temperature)
    {
        Assert.Throws<LayaCalibrationException>(
            () => LayaCalibration.Softmax(new[] { 1f, 2f }, temperature));
    }

    /// <summary>驗證有效 logits 含非有限值時不會被當成 masked padding。</summary>
    [Fact]
    public void Softmax_RejectsNonFiniteLogit()
    {
        Assert.Throws<LayaCalibrationException>(
            () => LayaCalibration.Softmax(new[] { float.NaN, 1f }, 1));
    }
}
