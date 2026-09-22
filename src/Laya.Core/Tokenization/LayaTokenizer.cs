using System.Text.Json;
using System.Text.Json.Serialization;
using Laya.Core.Exceptions;
using Tokenizers.HuggingFace.Tokenizer;

namespace Laya.Core.Tokenization;

/// <summary>擁有單一本機 Hugging Face tokenizer 並提供 reference-compatible 編碼。</summary>
public sealed class LayaTokenizer : IDisposable
{
    private Tokenizer? _tokenizer;

    /// <summary>建立已載入的 tokenizer wrapper。</summary>
    private LayaTokenizer(
        Tokenizer tokenizer,
        int clsTokenId,
        int sepTokenId,
        int padTokenId,
        int maskTokenId,
        int unkTokenId,
        string maskToken)
    {
        _tokenizer = tokenizer;
        ClsTokenId = clsTokenId;
        SepTokenId = sepTokenId;
        PadTokenId = padTokenId;
        MaskTokenId = maskTokenId;
        UnkTokenId = unkTokenId;
        MaskToken = maskToken;
    }

    /// <summary>取得 [CLS] token ID。</summary>
    public int ClsTokenId { get; }

    /// <summary>取得 [SEP] token ID。</summary>
    public int SepTokenId { get; }

    /// <summary>取得 [PAD] token ID。</summary>
    public int PadTokenId { get; }

    /// <summary>取得 [MASK] token ID。</summary>
    public int MaskTokenId { get; }

    /// <summary>取得 [UNK] token ID。</summary>
    public int UnkTokenId { get; }

    /// <summary>取得 reference scrub 規則使用的 mask token 文字。</summary>
    public string MaskToken { get; }

    /// <summary>從 tokenizer.json 與 tokenizer_config.json 建立 tokenizer。</summary>
    public static LayaTokenizer Load(string tokenizerPath, string tokenizerConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenizerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenizerConfigPath);

        if (!File.Exists(tokenizerPath))
        {
            throw new LayaTokenizationException(
                $"Tokenizer file '{tokenizerPath}' does not exist.",
                tokenizerPath);
        }

        if (!File.Exists(tokenizerConfigPath))
        {
            throw new LayaTokenizationException(
                $"Tokenizer config '{tokenizerConfigPath}' does not exist.",
                tokenizerPath);
        }

        Tokenizer? tokenizer = null;

        try
        {
            var config = ReadConfig(tokenizerConfigPath);
            tokenizer = Tokenizer.FromFile(tokenizerPath);

            var clsTokenId = ResolveTokenId(tokenizer, config.ClsToken!, tokenizerPath);
            var sepTokenId = ResolveTokenId(tokenizer, config.SepToken!, tokenizerPath);
            var padTokenId = ResolveTokenId(tokenizer, config.PadToken!, tokenizerPath);
            var maskTokenId = ResolveTokenId(tokenizer, config.MaskToken!, tokenizerPath);
            var unkTokenId = ResolveTokenId(tokenizer, config.UnkToken!, tokenizerPath);

            var wrapper = new LayaTokenizer(
                tokenizer,
                clsTokenId,
                sepTokenId,
                padTokenId,
                maskTokenId,
                unkTokenId,
                config.MaskToken!);
            tokenizer = null;
            return wrapper;
        }
        catch (LayaException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LayaTokenizationException(
                $"Could not load tokenizer '{tokenizerPath}'.",
                tokenizerPath,
                innerException: exception);
        }
        finally
        {
            tokenizer?.Dispose();
        }
    }

    /// <summary>以不自動加入 special tokens 的模式編碼單一文字。</summary>
    public IReadOnlyList<int> Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            var encoding = GetTokenizer()
                .Encode(text, addSpecialTokens: false)
                .FirstOrDefault();

            if (encoding is null)
            {
                throw new LayaTokenizationException(
                    "Tokenizer returned no encoding.");
            }

            return encoding.Ids
                .Select(id => checked((int)id))
                .ToArray();
        }
        catch (LayaException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LayaTokenizationException(
                "Tokenizer encoding failed.",
                innerException: exception);
        }
    }

    /// <summary>釋放 tokenizer 的 native handle；重複呼叫不會重複釋放。</summary>
    public void Dispose()
    {
        var tokenizer = Interlocked.Exchange(ref _tokenizer, null);
        tokenizer?.Dispose();
    }

    /// <summary>讀取並驗證 tokenizer_config.json 的 special token 欄位。</summary>
    private static TokenizerConfig ReadConfig(string path)
    {
        try
        {
            var config = JsonSerializer.Deserialize<TokenizerConfig>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = false });

            if (config is null ||
                string.IsNullOrWhiteSpace(config.ClsToken) ||
                string.IsNullOrWhiteSpace(config.SepToken) ||
                string.IsNullOrWhiteSpace(config.PadToken) ||
                string.IsNullOrWhiteSpace(config.MaskToken) ||
                string.IsNullOrWhiteSpace(config.UnkToken))
            {
                throw new LayaTokenizationException(
                    $"Tokenizer config '{path}' must define cls, sep, pad, mask and unk tokens.");
            }

            return config;
        }
        catch (LayaException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LayaTokenizationException(
                $"Could not read tokenizer config '{path}'.",
                innerException: exception);
        }
    }

    /// <summary>透過 tokenizer 本身解析 token ID，避免將 vocabulary ID 寫死在 production code。</summary>
    private static int ResolveTokenId(Tokenizer tokenizer, string token, string tokenizerPath)
    {
        var ids = tokenizer
            .Encode(token, addSpecialTokens: false)
            .FirstOrDefault()?
            .Ids
            .Select(id => checked((int)id))
            .ToArray();

        if (ids is null || ids.Length != 1)
        {
            throw new LayaTokenizationException(
                $"Special token '{token}' does not resolve to exactly one token ID.",
                tokenizerPath);
        }

        return ids[0];
    }

    /// <summary>取得仍可使用的 native tokenizer，已釋放時拋出明確錯誤。</summary>
    private Tokenizer GetTokenizer()
    {
        return _tokenizer ?? throw new ObjectDisposedException(nameof(LayaTokenizer));
    }

    /// <summary>保存 tokenizer_config.json 所需的 special token 名稱。</summary>
    private sealed class TokenizerConfig
    {
        /// <summary>取得 [CLS] 設定值。</summary>
        [JsonPropertyName("cls_token")]
        public string? ClsToken { get; init; }

        /// <summary>取得 [SEP] 設定值。</summary>
        [JsonPropertyName("sep_token")]
        public string? SepToken { get; init; }

        /// <summary>取得 [PAD] 設定值。</summary>
        [JsonPropertyName("pad_token")]
        public string? PadToken { get; init; }

        /// <summary>取得 [MASK] 設定值。</summary>
        [JsonPropertyName("mask_token")]
        public string? MaskToken { get; init; }

        /// <summary>取得 [UNK] 設定值。</summary>
        [JsonPropertyName("unk_token")]
        public string? UnkToken { get; init; }
    }
}
