using Laya.Core.Abstractions;
using Laya.Core.Encoding;
using Laya.Core.Exceptions;
using Laya.Core.Models;
using Laya.Core.PostProcessing;
using Laya.Core.Tokenization;

namespace Laya.Core.Inference;

/// <summary>協調 request validation、sequence、單次 inference 與 calibration 的同步 engine。</summary>
public sealed class LayaDecisionEngine : ILayaDecisionEngine, IDisposable
{
    private LayaOnnxSession? _session;
    private LayaTokenizer? _tokenizer;
    private readonly LayaSequenceBuilder _sequenceBuilder;
    private readonly LayaInferenceRunner _inferenceRunner;
    private readonly LayaPostProcessor _postProcessor;

    /// <summary>建立使用既有 session 與 tokenizer 的 decision engine。</summary>
    public LayaDecisionEngine(LayaOnnxSession session, LayaTokenizer tokenizer)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _sequenceBuilder = new LayaSequenceBuilder(tokenizer, session.Bundle.Config);
        _inferenceRunner = new LayaInferenceRunner(session);
        _postProcessor = new LayaPostProcessor();
    }

    /// <summary>取得建立 ONNX session 所花費的載入時間。</summary>
    public TimeSpan LoadDuration => GetSession().LoadDuration;

    /// <summary>載入 bundle、tokenizer 並建立可釋放的 decision engine。</summary>
    public static LayaDecisionEngine Open(string modelRoot)
    {
        var session = LayaOnnxSession.Open(modelRoot);
        LayaTokenizer? tokenizer = null;

        try
        {
            tokenizer = LayaTokenizer.Load(
                session.Bundle.TokenizerPath,
                session.Bundle.TokenizerConfigPath);
            return new LayaDecisionEngine(session, tokenizer);
        }
        catch
        {
            tokenizer?.Dispose();
            session.Dispose();
            throw;
        }
    }

    /// <summary>驗證 request、執行單次 batch Run 並回傳校準後答案。</summary>
    public LayaResult Decide(LayaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = GetSession();
        ValidateRequest(request);

        var batch = _sequenceBuilder.Build(request);
        var raw = _inferenceRunner.Run(batch);
        var answers = _postProcessor.Process(request, batch, raw, session.Bundle.Config);

        return new LayaResult(answers, raw.RunDuration);
    }

    /// <summary>釋放 tokenizer 與 ONNX session；重複呼叫不會重複釋放。</summary>
    public void Dispose()
    {
        var tokenizer = Interlocked.Exchange(ref _tokenizer, null);
        tokenizer?.Dispose();
        var session = Interlocked.Exchange(ref _session, null);
        session?.Dispose();
    }

    /// <summary>取得仍可使用的 session，已釋放時拋出明確錯誤。</summary>
    private LayaOnnxSession GetSession()
    {
        return _session ?? throw new ObjectDisposedException(nameof(LayaDecisionEngine));
    }

    /// <summary>驗證 request 的 question 數量、名稱、題型與 options。</summary>
    private static void ValidateRequest(LayaRequest request)
    {
        if (request.Questions.Count == 0)
        {
            throw new LayaConfigurationException(
                "Laya request must contain at least one question.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in request.Questions)
        {
            if (!names.Add(question.Name))
            {
                throw new LayaConfigurationException(
                    $"Question name '{question.Name}' is duplicated.",
                    questionName: question.Name);
            }

            if (!Enum.IsDefined(question.Type))
            {
                throw new LayaConfigurationException(
                    $"Unknown question type '{question.Type}'.",
                    questionName: question.Name);
            }

            if (question.Type == LayaQuestionType.Score)
            {
                throw new LayaConfigurationException(
                    "Score questions are not supported by this POC export.",
                    questionName: question.Name);
            }

            if (question.Type == LayaQuestionType.Noul && question.Options.Count > 0)
            {
                throw new LayaConfigurationException(
                    "Noul questions use reference-defined false/true options and cannot provide custom options.",
                    questionName: question.Name);
            }
        }
    }

}
