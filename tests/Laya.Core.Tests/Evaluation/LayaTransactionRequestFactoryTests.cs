using Laya.Core.Models;
using Laya.Core.Serialization;
using Laya.Core.Evaluation;

namespace Laya.Core.Tests.Evaluation;

[Trait("Category", "PureLogic")]
public sealed class LayaTransactionRequestFactoryTests
{
    /// <summary>驗證 baseline A 完整保留 Phase 1 的四欄 state、問句與 11 類順序。</summary>
    [Fact]
    public void CreateBaselineRequest_PreservesPhase1Contract()
    {
        var request = LayaTransactionRequestFactory.CreateBaselineRequest(
            "Coffee shop",
            -120.5,
            "TWD",
            "debit");

        Assert.Equal(
            "{\"description\": \"Coffee shop\", \"amount\": -120.5, \"currency\": \"TWD\", \"direction\": \"debit\"}",
            LayaStateSerializer.Serialize(request.State));
        Assert.Equal(2, request.Questions.Count);
        Assert.Equal("category", request.Questions[0].Name);
        Assert.Equal(LayaQuestionType.Choice, request.Questions[0].Type);
        Assert.Equal(LayaPhase2Labels.Categories, request.Questions[0].Options);
        Assert.Equal("Choose the best transaction category", request.Questions[0].Instructions);
        Assert.Equal("needs_review", request.Questions[1].Name);
        Assert.Equal(LayaQuestionType.Noul, request.Questions[1].Type);
        Assert.Equal("Is this transaction uncertain?", request.Questions[1].Instructions);
    }

    /// <summary>驗證三種 prompt 只有固定 A、B、C，且 options 與 state 不會變動。</summary>
    [Fact]
    public void CreateRequest_AllowsOnlyBoundedPromptVariants()
    {
        var input = new LayaTransactionInput("id", "Description", -1, "TWD", "debit");
        var baseline = LayaTransactionRequestFactory.CreateRequest(input, LayaPromptVariant.A);
        var categoryDefinition = LayaTransactionRequestFactory.CreateRequest(input, LayaPromptVariant.B);
        var specificCategory = LayaTransactionRequestFactory.CreateRequest(input, LayaPromptVariant.C);

        Assert.NotEqual(
            baseline.Questions[0].Instructions,
            categoryDefinition.Questions[0].Instructions);
        Assert.NotEqual(
            categoryDefinition.Questions[0].Instructions,
            specificCategory.Questions[0].Instructions);
        Assert.Equal(baseline.Questions[0].Options, categoryDefinition.Questions[0].Options);
        Assert.Equal(
            LayaStateSerializer.Serialize(baseline.State),
            LayaStateSerializer.Serialize(specificCategory.State));
    }
}
