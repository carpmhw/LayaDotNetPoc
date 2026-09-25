using Laya.Core.Encoding;
using Laya.Core.Inference;
using Laya.Core.Models;
using Microsoft.ML.OnnxRuntime;

namespace Laya.Core.Tests.Inference;

[Trait("Category", "PureLogic")]
public sealed class LayaInputTensorOwnerTests
{
    /// <summary>驗證 owner 建立五項必要輸入並於重複釋放時只 dispose 一次。</summary>
    [Fact]
    public void Create_OwnsFiveInputsAndDisposesThemOnce()
    {
        var disposedCount = 0;
        using var owner = LayaInputTensorOwner.Create(
            CreateBatch(),
            CreateValue,
            value =>
            {
                disposedCount++;
                value.Dispose();
            });

        Assert.Equal(
            new[] { "attention_mask", "input_ids", "marker_mask", "marker_pos", "qtype" },
            owner.Inputs.Keys.OrderBy(name => name, StringComparer.Ordinal));

        owner.Dispose();
        owner.Dispose();

        Assert.Equal(5, disposedCount);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = owner.Inputs;
        });
    }

    /// <summary>驗證建立中途失敗時已建立的 OrtValue 都會立即釋放。</summary>
    [Fact]
    public void Create_DisposesPreviouslyCreatedValuesWhenFactoryThrows()
    {
        var disposedCount = 0;
        var createdCount = 0;

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = LayaInputTensorOwner.Create(
                CreateBatch(),
                (memory, shape) =>
                {
                    createdCount++;
                    if (createdCount == 3)
                    {
                        throw new InvalidOperationException("synthetic tensor factory failure");
                    }

                    return CreateValue(memory, shape);
                },
                value =>
                {
                    disposedCount++;
                    value.Dispose();
                });
        });

        Assert.Equal("synthetic tensor factory failure", exception.Message);
        Assert.Equal(2, disposedCount);
    }

    /// <summary>建立供 tensor owner 測試使用的單列二 marker batch。</summary>
    private static LayaInputBatch CreateBatch()
    {
        var question = new LayaBatchQuestion("category", LayaQuestionType.Choice, new[] { 1, 2 }, new[] { 0, 1 });
        return new LayaInputBatch(
            sequenceLength: 2,
            markerCount: 2,
            inputIds: new long[] { 1, 2 },
            attentionMask: new[] { true, true },
            markerPositions: new long[] { 0, 1 },
            markerMask: new[] { true, true },
            questionTypes: new long[] { 0 },
            questions: new[] { question });
    }

    /// <summary>依測試輸入陣列型別建立實際 ONNX Runtime tensor value。</summary>
    private static OrtValue CreateValue(Array memory, long[] shape)
    {
        return memory switch
        {
            long[] values => OrtValue.CreateTensorValueFromMemory(values, shape),
            bool[] values => OrtValue.CreateTensorValueFromMemory(values, shape),
            _ => throw new ArgumentException("Unexpected tensor element type.", nameof(memory))
        };
    }
}
