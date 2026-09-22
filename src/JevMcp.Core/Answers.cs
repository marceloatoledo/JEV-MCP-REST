namespace JevMcp.Core;

/// <summary>Judgment primitive used in the question that produced the answer.</summary>
public enum JevAnswerKind
{
    Unknown = 0,
    Choice,
    Score,
    Noul,
}

/// <summary>
/// Model answer already deserialized. <c>Core</c> does not know the provider transport
/// format; the provider layer fills this model.
/// </summary>
public sealed record JevAnswer
{
    public JevAnswerKind Kind { get; init; }

    public string? Choice { get; init; }

    public double? Score { get; init; }

    public double? Noul { get; init; }

    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    public double? Confidence { get; init; }

    public IReadOnlyDictionary<string, string>? Legend { get; init; }
}

/// <summary>Choice answer that satisfied the contract.</summary>
public sealed record ChoiceAnswer(
    string Choice,
    IReadOnlyDictionary<string, double> Probabilities,
    double? Confidence);

/// <summary>Score answer that satisfied the contract.</summary>
public sealed record ScoreAnswer(double Score, double? Confidence);

/// <summary>
/// Validation of the three primitives. A malformed answer returns <c>null</c> and never
/// becomes a semantic result.
/// </summary>
public static class Answers
{
    /// <summary>
    /// Highest accepted score. The original rubrics have three levels, so the range is 0..2;
    /// accepting values outside it would let the model extrapolate the rubric.
    /// </summary>
    public const double MaxRubricScore = 2;

    /// <summary>
    /// Choice contract: the choice is among the expected keys, probability keys are
    /// exactly the expected ones, each probability is finite in [0,1], the sum closes
    /// within tolerance, and the choice is tied at the maximum.
    /// </summary>
    public static ChoiceAnswer? ValidateChoice(JevAnswer? answer, IEnumerable<string> expectedKeys)
    {
        if (answer?.Choice is not { } choice || answer.Probabilities is not { } probabilities)
        {
            return null;
        }

        var expected = new HashSet<string>(expectedKeys, StringComparer.Ordinal);
        if (!expected.Contains(choice) || probabilities.Count != expected.Count)
        {
            return null;
        }

        var sum = 0d;
        var maximum = double.NegativeInfinity;
        foreach (var (key, probability) in probabilities)
        {
            if (!expected.Contains(key) || !double.IsFinite(probability) || probability < 0 || probability > 1)
            {
                return null;
            }

            sum += probability;
            maximum = Math.Max(maximum, probability);
        }

        if (Math.Abs(sum - 1) > Limits.ProbabilitySumTolerance)
        {
            return null;
        }

        if (!probabilities.TryGetValue(choice, out var chosen) ||
            chosen < maximum - Limits.MaximumProbabilityTolerance)
        {
            return null;
        }

        return new ChoiceAnswer(choice, probabilities, NormalizeConfidence(answer.Confidence));
    }

    /// <summary>Score contract: finite score within the rubric, optional confidence.</summary>
    public static ScoreAnswer? ValidateScore(JevAnswer? answer)
    {
        if (answer?.Score is not { } score || !double.IsFinite(score) || score < 0 || score > MaxRubricScore)
        {
            return null;
        }

        return new ScoreAnswer(score, NormalizeConfidence(answer.Confidence));
    }

    /// <summary>Noul contract: finite probability in [0,1]. Zero is a valid answer.</summary>
    public static double? ValidateNoul(JevAnswer? answer)
    {
        if (answer?.Noul is not { } noul || !double.IsFinite(noul) || noul < 0 || noul > 1)
        {
            return null;
        }

        return noul;
    }

    /// <summary>
    /// Confidence outside [0,1] or non-finite is unknown, not zero. The distinction
    /// matters because unknown confidence never satisfies a threshold.
    /// </summary>
    public static double? NormalizeConfidence(double? confidence)
    {
        return confidence is { } value && double.IsFinite(value) && value >= 0 && value <= 1 ? value : null;
    }
}
