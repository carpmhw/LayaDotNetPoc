using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Laya.Core.Exceptions;

namespace Laya.Core.Serialization;

/// <summary>依照 @receptron/laya reference 將 state 轉成穩定文字。</summary>
public static class LayaStateSerializer
{
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>序列化 string 或 JSON-like state；object key 順序不重新排序。</summary>
    public static string Serialize(object? state)
    {
        try
        {
            if (state is string text)
            {
                return text;
            }

            var builder = new StringBuilder();
            var activeObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
            AppendValue(builder, state, activeObjects);
            return builder.ToString();
        }
        catch (LayaException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LayaConfigurationException(
                "Could not serialize Laya state.",
                innerException: exception);
        }
    }

    /// <summary>遞迴輸出單一 JSON-like value 並追蹤循環參照。</summary>
    private static void AppendValue(
        StringBuilder builder,
        object? value,
        HashSet<object> activeObjects)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        if (value is JsonElement jsonElement)
        {
            AppendJsonElement(builder, jsonElement, activeObjects);
            return;
        }

        if (value is string text)
        {
            builder.Append(JsonSerializer.Serialize(text, StringOptions));
            return;
        }

        if (value is char character)
        {
            builder.Append(JsonSerializer.Serialize(character.ToString(), StringOptions));
            return;
        }

        if (TryFormatNumber(value, out var number))
        {
            builder.Append(number);
            return;
        }

        if (value is bool boolean)
        {
            builder.Append(boolean ? "true" : "false");
            return;
        }

        if (!value.GetType().IsValueType && !activeObjects.Add(value))
        {
            throw new LayaConfigurationException("Laya state contains a circular reference.");
        }

        try
        {
            switch (value)
            {
                case IDictionary dictionary:
                    AppendDictionary(builder, dictionary, activeObjects);
                    return;
                case IEnumerable sequence:
                    AppendSequence(builder, sequence, activeObjects);
                    return;
                default:
                    AppendObject(builder, value, activeObjects);
                    return;
            }
        }
        finally
        {
            if (!value.GetType().IsValueType)
            {
                activeObjects.Remove(value);
            }
        }
    }

    /// <summary>輸出 JsonElement 並遵守 reference 的空白與 key 順序。</summary>
    private static void AppendJsonElement(
        StringBuilder builder,
        JsonElement value,
        HashSet<object> activeObjects)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                builder.Append("null");
                return;
            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(value.GetString(), StringOptions));
                return;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var integer))
                {
                    builder.Append(integer.ToString(CultureInfo.InvariantCulture));
                    return;
                }

                if (!value.TryGetDouble(out var floating) || !double.IsFinite(floating))
                {
                    throw new LayaConfigurationException(
                        "Laya state contains a non-finite number.");
                }

                builder.Append(floating.ToString("R", CultureInfo.InvariantCulture));
                return;
            case JsonValueKind.True:
                builder.Append("true");
                return;
            case JsonValueKind.False:
                builder.Append("false");
                return;
            case JsonValueKind.Array:
                builder.Append('[');
                var firstArrayValue = true;
                foreach (var item in value.EnumerateArray())
                {
                    AppendSeparator(builder, ref firstArrayValue, ',');
                    AppendJsonElement(builder, item, activeObjects);
                }

                builder.Append(']');
                return;
            case JsonValueKind.Object:
                builder.Append('{');
                var firstProperty = true;
                foreach (var property in value.EnumerateObject())
                {
                    AppendSeparator(builder, ref firstProperty, ',');
                    AppendJsonPropertyName(builder, property.Name);
                    AppendJsonElement(builder, property.Value, activeObjects);
                }

                builder.Append('}');
                return;
            default:
                throw new LayaConfigurationException(
                    $"Unsupported JSON state value kind '{value.ValueKind}'.");
        }
    }

    /// <summary>輸出 IDictionary，保留其列舉順序並要求 string key。</summary>
    private static void AppendDictionary(
        StringBuilder builder,
        IDictionary dictionary,
        HashSet<object> activeObjects)
    {
        builder.Append('{');
        var first = true;

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
            {
                throw new LayaConfigurationException(
                    "Laya state dictionary keys must be strings.");
            }

            AppendSeparator(builder, ref first, ',');
            AppendJsonPropertyName(builder, key);
            AppendValue(builder, entry.Value, activeObjects);
        }

        builder.Append('}');
    }

    /// <summary>輸出 enumerable，保留 array 或 list 的原始順序。</summary>
    private static void AppendSequence(
        StringBuilder builder,
        IEnumerable sequence,
        HashSet<object> activeObjects)
    {
        builder.Append('[');
        var first = true;

        foreach (var item in sequence)
        {
            AppendSeparator(builder, ref first, ',');
            AppendValue(builder, item, activeObjects);
        }

        builder.Append(']');
    }

    /// <summary>輸出一般 object 的 public readable properties。</summary>
    private static void AppendObject(
        StringBuilder builder,
        object value,
        HashSet<object> activeObjects)
    {
        var properties = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToArray();

        builder.Append('{');
        var first = true;
        foreach (var property in properties)
        {
            AppendSeparator(builder, ref first, ',');
            var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
            AppendJsonPropertyName(builder, jsonName);
            AppendValue(builder, property.GetValue(value), activeObjects);
        }

        builder.Append('}');
    }

    /// <summary>輸出 JSON property name。</summary>
    private static void AppendJsonPropertyName(StringBuilder builder, string name)
    {
        builder.Append(JsonSerializer.Serialize(name, StringOptions));
        builder.Append(": ");
    }

    /// <summary>輸出帶有 reference 空白規則的 separator。</summary>
    private static void AppendSeparator(StringBuilder builder, ref bool first, char separator)
    {
        if (!first)
        {
            builder.Append(separator);
            builder.Append(' ');
        }

        first = false;
    }

    /// <summary>格式化支援的數值型別並拒絕非有限浮點值。</summary>
    private static bool TryFormatNumber(object value, out string formatted)
    {
        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                formatted = Convert.ToString(value, CultureInfo.InvariantCulture)!;
                return true;
            case decimal decimalValue:
                formatted = decimalValue.ToString("G29", CultureInfo.InvariantCulture);
                return true;
            case float floatValue when float.IsFinite(floatValue):
                formatted = floatValue.ToString("R", CultureInfo.InvariantCulture);
                return true;
            case double doubleValue when double.IsFinite(doubleValue):
                formatted = doubleValue.ToString("R", CultureInfo.InvariantCulture);
                return true;
            case float or double:
                throw new LayaConfigurationException(
                    "Laya state contains a non-finite number.");
            default:
                formatted = string.Empty;
                return false;
        }
    }

    /// <summary>提供以 reference identity 判斷循環參照的 comparer。</summary>
    private sealed class ReferenceEqualityComparer : EqualityComparer<object>
    {
        /// <summary>取得單一 comparer instance。</summary>
        public static ReferenceEqualityComparer Instance { get; } = new();

        /// <summary>以 object reference 判斷相等。</summary>
        public override bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        /// <summary>取得 object identity hash code。</summary>
        public override int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
