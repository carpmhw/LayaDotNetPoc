using Laya.Core.Exceptions;

namespace Laya.Core.Evaluation;

/// <summary>表示 POC confidence policy 的三種 category decision mode。</summary>
public enum LayaDecisionMode
{
    /// <summary>高機率且具有足夠 margin，可自動化。</summary>
    Auto,

    /// <summary>可提出建議但仍需人工確認。</summary>
    Suggest,

    /// <summary>需要人工 review。</summary>
    Review
}

/// <summary>套用文件定義的暫定 confidence 與 margin 門檻。</summary>
public static class LayaConfidencePolicy
{
    /// <summary>依 category probability 與 top1-top2 margin 分類 decision mode。</summary>
    public static LayaDecisionMode Classify(double probability, double margin)
    {
        if (!double.IsFinite(probability) || probability < 0 || probability > 1 ||
            !double.IsFinite(margin) || margin < 0 || margin > 1)
        {
            throw new LayaConfigurationException(
                "Confidence probability and margin must be finite values in [0,1].");
        }

        if (probability >= 0.85 && margin >= 0.20)
        {
            return LayaDecisionMode.Auto;
        }

        if (probability >= 0.85)
        {
            return LayaDecisionMode.Review;
        }

        return probability >= 0.60
            ? LayaDecisionMode.Suggest
            : LayaDecisionMode.Review;
    }
}
