using Laya.Core.Exceptions;

namespace Laya.Core.PostProcessing;

/// <summary>提供 reference-compatible temperature calibration 與 stable softmax。</summary>
public static class LayaCalibration
{
    /// <summary>對有效 logits 套用 temperature 並回傳 normalized probabilities。</summary>
    public static double[] Softmax(IReadOnlyList<float> logits, double temperature)
    {
        ArgumentNullException.ThrowIfNull(logits);

        if (logits.Count < 2)
        {
            throw new LayaCalibrationException(
                "At least two valid logits are required for calibration.",
                optionCount: logits.Count);
        }

        if (!double.IsFinite(temperature) || temperature <= 0)
        {
            throw new LayaCalibrationException(
                "Calibration temperature must be finite and greater than zero.",
                optionCount: logits.Count);
        }

        var scaled = new double[logits.Count];
        var maximum = double.NegativeInfinity;
        for (var index = 0; index < logits.Count; index++)
        {
            if (!float.IsFinite(logits[index]))
            {
                throw new LayaCalibrationException(
                    $"Logit at index {index} is not finite.",
                    optionCount: logits.Count);
            }

            scaled[index] = logits[index] / temperature;
            maximum = Math.Max(maximum, scaled[index]);
        }

        var exponentials = new double[logits.Count];
        var sum = 0d;
        for (var index = 0; index < scaled.Length; index++)
        {
            exponentials[index] = Math.Exp(scaled[index] - maximum);
            sum += exponentials[index];
        }

        if (!double.IsFinite(sum) || sum <= 0)
        {
            throw new LayaCalibrationException(
                "Stable softmax produced an invalid normalization sum.",
                optionCount: logits.Count);
        }

        for (var index = 0; index < exponentials.Length; index++)
        {
            exponentials[index] /= sum;
        }

        return exponentials;
    }
}
