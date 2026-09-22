using System.Globalization;
using Laya.Core.Exceptions;
using Laya.Core.Serialization;

namespace Laya.Core.Tests.Serialization;

public sealed class LayaStateSerializerTests
{
    /// <summary>驗證 string state 直接作為文字，不被再次包成 JSON 字串。</summary>
    [Fact]
    public void Serialize_StringReturnsOriginalText()
    {
        Assert.Equal("raw transaction text", LayaStateSerializer.Serialize("raw transaction text"));
    }

    /// <summary>驗證 object state 保留插入順序、中文與 reference separators。</summary>
    [Fact]
    public void Serialize_ObjectMatchesReferenceJsonStyle()
    {
        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["merchant"] = "全家",
            ["amount"] = 12.5,
            ["items"] = new object?[] { "coffee", null }
        };

        Assert.Equal(
            "{\"merchant\": \"全家\", \"amount\": 12.5, \"items\": [\"coffee\", null]}",
            LayaStateSerializer.Serialize(state));
    }

    /// <summary>驗證巢狀 dictionary 與 array 保留各自的順序。</summary>
    [Fact]
    public void Serialize_NestedValuesPreserveOrder()
    {
        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["outer"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["z"] = 1,
                ["a"] = true
            }
        };

        Assert.Equal(
            "{\"outer\": {\"z\": 1, \"a\": true}}",
            LayaStateSerializer.Serialize(state));
    }

    /// <summary>驗證數值格式不受目前 thread culture 的小數分隔符影響。</summary>
    [Fact]
    public void Serialize_NumbersUseInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var state = new Dictionary<string, object?> { ["amount"] = 12.5m };

            Assert.Equal("{\"amount\": 12.5}", LayaStateSerializer.Serialize(state));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>驗證非有限浮點值會被拒絕，不輸出看似合法的 null。</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Serialize_RejectsNonFiniteNumbers(double value)
    {
        Assert.Throws<LayaConfigurationException>(
            () => LayaStateSerializer.Serialize(new Dictionary<string, object?> { ["value"] = value }));
    }

    /// <summary>驗證循環參照會被拒絕，避免 serializer 無限遞迴。</summary>
    [Fact]
    public void Serialize_RejectsCycles()
    {
        var state = new Dictionary<string, object?>(StringComparer.Ordinal);
        state["self"] = state;

        Assert.Throws<LayaConfigurationException>(() => LayaStateSerializer.Serialize(state));
    }
}
