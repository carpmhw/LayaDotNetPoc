using System.Text.Json;
using System.Text.Json.Serialization;
using Laya.Core.Exceptions;

namespace Laya.Core.Configuration;

/// <summary>表示 laya_config.json 的固定輸入設定。</summary>
public sealed class LayaModelConfig
{
    /// <summary>取得 state sequence 的最大 token 數。</summary>
    [JsonPropertyName("max_len")]
    public int MaxLength { get; init; }

    /// <summary>取得 question header 與 options 的最大 token 預算。</summary>
    [JsonPropertyName("head_max_len")]
    public int HeadMaxLength { get; init; }

    /// <summary>取得依題型索引的 fallback temperature。</summary>
    [JsonPropertyName("temperature")]
    public IReadOnlyList<double> Temperature { get; init; } = Array.Empty<double>();

    /// <summary>取得依題型與 option cardinality 的校準 temperature。</summary>
    [JsonPropertyName("temperature_by_options")]
    public IReadOnlyDictionary<string, double> TemperatureByOptions { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>從指定 JSON 檔載入並驗證模型設定。</summary>
    public static LayaModelConfig Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<LayaModelConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = false });

            if (config is null)
            {
                throw new LayaConfigurationException($"Model configuration '{path}' is empty.");
            }

            config.Validate(path);
            return config;
        }
        catch (LayaConfigurationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LayaConfigurationException(
                $"Could not read model configuration '{path}'.",
                innerException: exception);
        }
    }

    /// <summary>依題型 key 與 option 數量取得 reference temperature。</summary>
    public double GetTemperature(string questionType, int optionCount)
    {
        var key = $"{questionType}:{GetSizeBucket(optionCount)}";

        if (TemperatureByOptions.TryGetValue(key, out var specificTemperature))
        {
            return ValidateTemperature(specificTemperature, key);
        }

        var index = questionType switch
        {
            "choice" => 0,
            "score" => 1,
            "noul" => 2,
            _ => throw new LayaConfigurationException($"Unknown question type '{questionType}'.")
        };

        if (index >= Temperature.Count)
        {
            throw new LayaConfigurationException(
                $"Fallback temperature for '{questionType}' is missing.");
        }

        return ValidateTemperature(Temperature[index], questionType);
    }

    /// <summary>驗證所有必要設定欄位與數值範圍。</summary>
    private void Validate(string path)
    {
        if (MaxLength <= 0)
        {
            throw new LayaConfigurationException(
                $"Configuration '{path}' must define a positive max_len.");
        }

        if (HeadMaxLength <= 0 || HeadMaxLength > MaxLength)
        {
            throw new LayaConfigurationException(
                $"Configuration '{path}' must define head_max_len between 1 and max_len.");
        }

        if (Temperature.Count < 3)
        {
            throw new LayaConfigurationException(
                $"Configuration '{path}' must define choice, score and noul temperatures.");
        }

        foreach (var value in Temperature)
        {
            ValidateTemperature(value, "temperature");
        }

        foreach (var item in TemperatureByOptions)
        {
            ValidateTemperature(item.Value, item.Key);
        }
    }

    /// <summary>驗證單一 temperature 並回傳原值。</summary>
    private static double ValidateTemperature(double value, string key)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new LayaConfigurationException(
                $"Temperature '{key}' must be finite and greater than zero.");
        }

        return value;
    }

    /// <summary>將 option 數量轉換為 upstream 使用的 cardinality bucket。</summary>
    private static string GetSizeBucket(int optionCount)
    {
        if (optionCount <= 0)
        {
            throw new LayaConfigurationException("Option count must be greater than zero.");
        }

        return optionCount switch
        {
            <= 2 => "2",
            <= 5 => "3-5",
            <= 10 => "6-10",
            _ => "11+"
        };
    }
}
