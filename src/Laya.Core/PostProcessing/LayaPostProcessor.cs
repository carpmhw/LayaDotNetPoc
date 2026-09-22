using Laya.Core.Configuration;
using Laya.Core.Encoding;
using Laya.Core.Exceptions;
using Laya.Core.Inference;
using Laya.Core.Models;

namespace Laya.Core.PostProcessing;

/// <summary>將 raw logits 依 question type、temperature 與 option 順序映射成答案。</summary>
public sealed class LayaPostProcessor
{
    /// <summary>校準一個 request 的 raw outputs 並產生保序答案。</summary>
    public IReadOnlyList<LayaAnswer> Process(
        LayaRequest request,
        LayaInputBatch batch,
        LayaInferenceResult inference,
        LayaModelConfig config)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(inference);
        ArgumentNullException.ThrowIfNull(config);

        if (request.Questions.Count != batch.BatchSize ||
            inference.Logits.Length < batch.BatchSize * batch.MarkerCount ||
            inference.ActProbabilities.Length < batch.BatchSize * 2)
        {
            throw new LayaCalibrationException(
                "Raw inference output shape does not match the request batch.");
        }

        var answers = new List<LayaAnswer>(request.Questions.Count);
        for (var row = 0; row < request.Questions.Count; row++)
        {
            var question = request.Questions[row];
            var options = GetAnswerOptions(question);
            var logits = new float[options.Count];
            Array.Copy(inference.Logits, row * batch.MarkerCount, logits, 0, options.Count);
            var temperature = config.GetTemperature(GetTypeName(question.Type), options.Count);
            var probabilities = LayaCalibration.Softmax(logits, temperature);
            var selectedIndex = SelectMaximum(probabilities);
            var probability = question.Type == LayaQuestionType.Noul
                ? probabilities[1]
                : probabilities[selectedIndex];
            var probabilityMap = options
                .Select((option, index) => new KeyValuePair<string, double>(option, probabilities[index]))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

            answers.Add(
                new LayaAnswer(
                    question.Name,
                    options[selectedIndex],
                    probability,
                    probabilityMap));
        }

        return answers;
    }

    /// <summary>取得題型對應的 output option labels。</summary>
    private static IReadOnlyList<string> GetAnswerOptions(LayaQuestion question)
    {
        return question.Type == LayaQuestionType.Noul
            ? new[] { "false", "true" }
            : question.Options;
    }

    /// <summary>取得 config temperature lookup 使用的小寫題型名稱。</summary>
    private static string GetTypeName(LayaQuestionType type)
    {
        return type switch
        {
            LayaQuestionType.Choice => "choice",
            LayaQuestionType.Score => "score",
            LayaQuestionType.Noul => "noul",
            _ => throw new LayaConfigurationException($"Unknown question type '{type}'.")
        };
    }

    /// <summary>以固定 first-max 規則選取最高 probability option。</summary>
    private static int SelectMaximum(IReadOnlyList<double> probabilities)
    {
        var bestIndex = 0;
        for (var index = 1; index < probabilities.Count; index++)
        {
            if (probabilities[index] > probabilities[bestIndex])
            {
                bestIndex = index;
            }
        }

        return bestIndex;
    }
}
