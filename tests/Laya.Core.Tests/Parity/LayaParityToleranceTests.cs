using Laya.Core.Parity;

namespace Laya.Core.Tests.Parity;

[Trait("Category", "PureLogic")]
public sealed class LayaParityToleranceTests
{
    /// <summary>驗證答案相同但 probability 超過預設 tolerance 時仍判定不相容。</summary>
    [Fact]
    public void CompareProbability_RejectsNumericMismatchDespiteSameAnswer()
    {
        var result = LayaParityTolerance.CompareProbability(0.70000, 0.70101);

        Assert.False(result.IsWithinTolerance);
        Assert.Equal(0.00101, result.AbsoluteError, precision: 12);
    }

    /// <summary>驗證放寬 tolerance 必須有 provenance 且不得超過上限。</summary>
    [Fact]
    public void Validate_RequiresInvestigationForExplicitRelaxation()
    {
        Assert.Throws<ArgumentException>(() => LayaParityTolerance.Validate(0.0002, null));
        LayaParityTolerance.Validate(0.0002, "runtime investigation-1");
        Assert.Throws<ArgumentOutOfRangeException>(() => LayaParityTolerance.Validate(0.0006, "investigation"));
    }

    /// <summary>驗證有效 probability 的 NaN 或 Infinity 不能被 tolerance 掩蓋。</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CompareProbability_RejectsNonFiniteValues(double actual)
    {
        Assert.Throws<ArgumentException>(() => LayaParityTolerance.CompareProbability(0.5, actual));
    }
}
