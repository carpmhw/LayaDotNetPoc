using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

public sealed class LayaConfidencePolicyTests
{
    /// <summary>驗證 AUTO 需要同時滿足 probability 與 margin 門檻。</summary>
    [Theory]
    [InlineData(0.85, 0.20, LayaDecisionMode.Auto)]
    [InlineData(0.90, 0.19, LayaDecisionMode.Review)]
    [InlineData(0.60, 0.90, LayaDecisionMode.Suggest)]
    [InlineData(0.59, 0.90, LayaDecisionMode.Review)]
    public void Classify_UsesDocumentedBoundaries(
        double probability,
        double margin,
        LayaDecisionMode expected)
    {
        Assert.Equal(expected, LayaConfidencePolicy.Classify(probability, margin));
    }
}
