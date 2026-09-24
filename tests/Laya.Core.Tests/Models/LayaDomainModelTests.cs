using Laya.Core.Models;

namespace Laya.Core.Tests.Models;

[Trait("Category", "PureLogic")]
public sealed class LayaDomainModelTests
{
    /// <summary>驗證 question type numeric values 與 ONNX qtype 契約一致。</summary>
    [Fact]
    public void QuestionType_UsesReferenceValues()
    {
        Assert.Equal(0, (int)LayaQuestionType.Choice);
        Assert.Equal(1, (int)LayaQuestionType.Score);
        Assert.Equal(2, (int)LayaQuestionType.Noul);
    }

    /// <summary>驗證 request 保留 question 順序並複製輸入集合。</summary>
    [Fact]
    public void Request_PreservesQuestionOrder()
    {
        var questions = new List<LayaQuestion>
        {
            new("first", LayaQuestionType.Choice, "pick", new[] { "a", "b" }),
            new("second", LayaQuestionType.Noul, "is valid")
        };

        var request = new LayaRequest(new { merchant = "demo" }, questions);
        questions.Clear();

        Assert.Collection(
            request.Questions,
            first => Assert.Equal("first", first.Name),
            second => Assert.Equal("second", second.Name));
    }
}
