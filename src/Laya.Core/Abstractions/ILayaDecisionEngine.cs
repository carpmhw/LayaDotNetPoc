using Laya.Core.Models;

namespace Laya.Core.Abstractions;

/// <summary>定義同步 Laya decision engine API。</summary>
public interface ILayaDecisionEngine
{
    /// <summary>對一個 request 執行同步決策。</summary>
    LayaResult Decide(LayaRequest request);
}
