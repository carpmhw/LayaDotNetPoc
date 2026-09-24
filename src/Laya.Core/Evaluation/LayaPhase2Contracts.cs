using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace Laya.Core.Evaluation;

/// <summary>集中保存 Phase 2 request、serialization 與 policy 的可重算身分。</summary>
public static class LayaPhase2Contracts
{
    /// <summary>取得目前 state serialization 的固定契約版本。</summary>
    public const string SerializationVersion = "laya-state-v1";

    /// <summary>計算指定 prompt variant 的 UTF-8 SHA-256。</summary>
    public static string PromptHash(LayaPromptVariant variant)
    {
        return Hash(System.Text.Encoding.UTF8.GetBytes(LayaTransactionRequestFactory.GetCategoryPrompt(variant)));
    }

    /// <summary>計算固定十一個 options 順序的 SHA-256。</summary>
    public static string OptionOrderHash()
    {
        return Hash(JsonSerializer.SerializeToUtf8Bytes(LayaPhase2Labels.Categories));
    }

    /// <summary>計算 serializer 實作契約的固定 SHA-256。</summary>
    public static string SerializationHash()
    {
        const string contract =
            "LayaStateSerializer|laya-state-v1|preserve-object-order|invariant-number-format|unsafe-relaxed-json";
        return Hash(System.Text.Encoding.UTF8.GetBytes(contract));
    }

    /// <summary>以固定欄位順序計算 policy 的 SHA-256，避免 JSON 格式影響身分。</summary>
    public static string PolicyHash(LayaPhase2Policy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var canonical = string.Join(
            "|",
            policy.Name,
            policy.AutoEnabled ? "true" : "false",
            policy.AutoProbabilityThreshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            policy.AutoMarginThreshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            policy.SuggestProbabilityThreshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            policy.SuggestMarginThreshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return Hash(System.Text.Encoding.UTF8.GetBytes(canonical));
    }

    /// <summary>保存 .NET runtime、OS、架構與 processor 設定的環境指紋。</summary>
    public static string EnvironmentFingerprint()
    {
        var value = string.Join(
            "|",
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture,
            Environment.ProcessorCount);
        return Hash(System.Text.Encoding.UTF8.GetBytes(value));
    }

    /// <summary>以小寫十六進位保存位元組的 SHA-256。</summary>
    public static string Hash(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
