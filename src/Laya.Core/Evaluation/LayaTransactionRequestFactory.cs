using Laya.Core.Models;

namespace Laya.Core.Evaluation;

/// <summary>限制 Phase 2 只能使用計畫定義的三種 prompt variant。</summary>
public enum LayaPromptVariant
{
    /// <summary>Phase 1 原始 baseline prompt。</summary>
    A,

    /// <summary>加入固定 category definitions 的 prompt。</summary>
    B,

    /// <summary>要求最具體類別且避免 direction 泛化成 transfer 的 prompt。</summary>
    C
}

/// <summary>保存建立 Laya transaction request 所需的四欄 state。</summary>
public sealed record LayaTransactionInput(
    string Id,
    string Description,
    double Amount,
    string Currency,
    string Direction);

/// <summary>集中建立 baseline、prompt variants 與固定 options 的 transaction request。</summary>
public static class LayaTransactionRequestFactory
{
    /// <summary>建立完全相容 Phase 1 的 baseline A request。</summary>
    public static LayaRequest CreateBaselineRequest(
        string description,
        double amount,
        string currency,
        string direction)
    {
        return CreateRequest(
            new LayaTransactionInput(string.Empty, description, amount, currency, direction),
            LayaPromptVariant.A);
    }

    /// <summary>依固定 A、B、C variant 建立含 Choice 與 Noul 的 request。</summary>
    public static LayaRequest CreateRequest(
        LayaTransactionInput input,
        LayaPromptVariant variant = LayaPromptVariant.A)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(variant))
        {
            throw new ArgumentOutOfRangeException(nameof(variant));
        }

        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["description"] = input.Description,
            ["amount"] = input.Amount,
            ["currency"] = input.Currency,
            ["direction"] = input.Direction
        };
        return new LayaRequest(
            state,
            new[]
            {
                new LayaQuestion(
                    "category",
                    LayaQuestionType.Choice,
                    GetCategoryPrompt(variant),
                    LayaPhase2Labels.Categories),
                new LayaQuestion(
                    "needs_review",
                    LayaQuestionType.Noul,
                    "Is this transaction uncertain?")
            });
    }

    /// <summary>取得固定 variant 的 category prompt，不接受額外隱含版本。</summary>
    public static string GetCategoryPrompt(LayaPromptVariant variant)
    {
        return variant switch
        {
            LayaPromptVariant.A => "Choose the best transaction category",
            LayaPromptVariant.B =>
                "Choose the best transaction category using the category definitions",
            LayaPromptVariant.C =>
                "Choose the most specific transaction category; do not use transfer only because direction is debit or credit",
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
    }
}
