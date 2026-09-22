using System.Globalization;

namespace JevMcp.Core;

/// <summary>
/// Action the host agent should take from the judgment.
/// Numeric values order by severity, and <see cref="Policy.WorstAction"/> depends on that.
/// </summary>
public enum PolicyAction
{
    Auto = 0,
    Review = 1,
    Escalate = 2,
}

/// <summary>Verdict of a claim checked against evidence.</summary>
public enum ClaimVerdict
{
    Verified,
    Contradicted,
    Unsupported,
}

/// <summary>Outcome of screening untrusted content.</summary>
public enum ScreenAction
{
    Pass,
    Review,
    Block,
    Skip,
}

/// <summary>Document verdict for jev_find.</summary>
public enum ExistsVerdict
{
    Answered,
    Partial,
    Absent,
}

/// <summary>Verdict of a requirement checked against a jev_decide candidate.</summary>
public enum RequirementVerdict
{
    Supported,
    Contradicted,
    Unknown,
}

/// <summary>Screening recommendation with the reason returned to the caller.</summary>
public sealed record ScreenRecommendation(ScreenAction Action, string Reason);

/// <summary>Validated threshold pair.</summary>
public sealed record PolicyThresholds(double AutoAccept, double ReviewAt);

/// <summary>Patch-review rubrics, each in the 0..2 range.</summary>
public sealed record ReviewScores(double Correctness, double SpecMatch, double TestGap, double BlastRadius);

/// <summary>A requirement checked against a candidate.</summary>
public sealed record RequirementCheck(string Candidate, int Requirement, RequirementVerdict Answer);

/// <summary>
/// Review rubric weights. Correctness and spec match enter as-is; test gap and
/// blast radius are inverted first, so a large gap or wide radius lowers the composite.
/// </summary>
public static class ReviewWeights
{
    public const double Correctness = 0.4;
    public const double SpecMatch = 0.3;
    public const double TestGap = 0.15;
    public const double BlastRadius = 0.15;

    /// <summary>The same weights with payload keys, to return to the caller.</summary>
    public static readonly IReadOnlyDictionary<string, double> ByName = new Dictionary<string, double>(
        StringComparer.Ordinal)
    {
        ["correctness"] = Correctness,
        ["spec_match"] = SpecMatch,
        ["test_gap"] = TestGap,
        ["blast_radius"] = BlastRadius,
    };
}

/// <summary>
/// Decision arithmetic. Every limit is a parameter; nothing here calls an API.
/// </summary>
public static class Policy
{
    /// <summary>Default auto-accept threshold.</summary>
    public const double DefaultAutoAccept = 0.8;

    /// <summary>Ceiling of the inferred review_at when the caller omits the pair.</summary>
    public const double DefaultReviewAtCeiling = 0.5;

    /// <summary>Default composite floor: auto requires the weighted composite from here up.</summary>
    public const double DefaultCompositeFloor = 0.7;

    /// <summary>Below this the text has too little substance to be worth reading.</summary>
    public const double SubstanceFloor = 0.3;

    /// <summary>Below this the text does not serve the stated purpose.</summary>
    public const double RelevanceFloor = 0.3;

    /// <summary>jev_find cut for "answered".</summary>
    public const double ExistsFoundAt = 0.7;

    /// <summary>jev_find cut for "absent".</summary>
    public const double ExistsAbsentAt = 0.35;

    /// <summary>Does the verdict stand on its own, or does it need human confirmation?</summary>
    public static PolicyAction VerifyAction(double confidence, double autoAccept)
    {
        return confidence >= autoAccept ? PolicyAction.Auto : PolicyAction.Review;
    }

    /// <summary>
    /// Screening from probabilities. <paramref name="injection"/> is the probability
    /// the text contains instructions aimed at an agent; relevance and substance are
    /// optional and only discard useless content after the injection test.
    /// </summary>
    public static ScreenRecommendation Screen(
        double injection,
        double blockAt,
        double reviewAt,
        double? relevance = null,
        double? substance = null)
    {
        if (injection >= blockAt)
        {
            return new ScreenRecommendation(
                ScreenAction.Block,
                $"injection probability {Fixed2(injection)} >= block threshold {Plain(blockAt)}");
        }

        if (injection >= reviewAt)
        {
            return new ScreenRecommendation(
                ScreenAction.Review,
                $"injection probability {Fixed2(injection)} >= review threshold {Plain(reviewAt)}");
        }

        if (substance is { } substanceValue && substanceValue < SubstanceFloor)
        {
            return new ScreenRecommendation(
                ScreenAction.Skip,
                $"little substantive content (substance {Fixed2(substanceValue)})");
        }

        if (relevance is { } relevanceValue && relevanceValue < RelevanceFloor)
        {
            return new ScreenRecommendation(
                ScreenAction.Skip,
                $"not relevant to the stated purpose (relevance {Fixed2(relevanceValue)})");
        }

        return new ScreenRecommendation(ScreenAction.Pass, "no signals above thresholds");
    }

    /// <summary>Converts the existence Noul into a document verdict (cookbook cuts).</summary>
    public static ExistsVerdict Exists(double exists, double found = ExistsFoundAt, double absent = ExistsAbsentAt)
    {
        if (exists >= found)
        {
            return ExistsVerdict.Answered;
        }

        return exists < absent ? ExistsVerdict.Absent : ExistsVerdict.Partial;
    }

    /// <summary>
    /// Distance from the winner to the runner-up. A lone probability has no runner-up,
    /// so its margin is zero.
    /// </summary>
    public static double Margin(IReadOnlyDictionary<string, double>? probabilities)
    {
        if (probabilities is null || probabilities.Count < 2)
        {
            return 0;
        }

        var ranked = probabilities.Values.OrderByDescending(value => value).ToArray();
        return ranked[0] - ranked[1];
    }

    /// <summary>
    /// Auto-accept requires both: high top probability and a clear margin.
    /// Either one alone already caused errors in the original classify tests.
    /// </summary>
    public static PolicyAction ClassificationDecision(
        double topProbability,
        double margin,
        double autoAccept,
        double minimumMargin)
    {
        return topProbability >= autoAccept && margin >= minimumMargin ? PolicyAction.Auto : PolicyAction.Review;
    }

    /// <summary>
    /// Reranking succeeded with a full score for every candidate, or the response was
    /// malformed and the ordering cannot be trusted.
    /// </summary>
    public static PolicyAction RerankAction(bool invalidResponse) =>
        invalidResponse ? PolicyAction.Escalate : PolicyAction.Auto;

    /// <summary>
    /// Whether the host can follow the recommendation without human confirmation.
    /// Escape hatches, requirement contradictions, and low confidence need review;
    /// an invalid model response escalates.
    /// </summary>
    public static PolicyAction DecideAction(
        bool invalidResponse,
        bool escaped,
        bool hasWarnings,
        double? confidence,
        double autoAccept = DefaultAutoAccept)
    {
        if (invalidResponse)
        {
            return PolicyAction.Escalate;
        }

        if (escaped || hasWarnings)
        {
            return PolicyAction.Review;
        }

        if (confidence is not { } value || value < autoAccept)
        {
            return PolicyAction.Review;
        }

        return PolicyAction.Auto;
    }

    /// <summary>
    /// Requirements that contradict the recommended candidate. Independent questions can
    /// disagree with the recommendation; the disagreement is exposed, not averaged away.
    /// </summary>
    public static IReadOnlyList<int> ContradictsRecommendation(
        IEnumerable<RequirementCheck> checks,
        string recommended)
    {
        return checks
            .Where(check =>
                string.Equals(check.Candidate, recommended, StringComparison.Ordinal) &&
                check.Answer == RequirementVerdict.Contradicted)
            .Select(check => check.Requirement)
            .ToArray();
    }

    /// <summary>Thresholds must satisfy <c>0 &lt;= review_at &lt;= auto_accept &lt;= 1</c>.</summary>
    /// <exception cref="ArgumentException">When the pair violates the order or the range.</exception>
    public static void ValidateThresholds(double autoAccept, double reviewAt)
    {
        if (!double.IsFinite(autoAccept) ||
            !double.IsFinite(reviewAt) ||
            autoAccept < 0 ||
            autoAccept > 1 ||
            reviewAt < 0 ||
            reviewAt > 1 ||
            reviewAt > autoAccept)
        {
            throw new ArgumentException("Thresholds must satisfy 0 <= review_at <= auto_accept <= 1.");
        }
    }

    /// <summary>Fills an omitted review_at so a low auto_accept cannot invert the pair.</summary>
    public static PolicyThresholds ResolveThresholds(double autoAccept = DefaultAutoAccept, double? reviewAt = null)
    {
        var resolved = reviewAt ?? Math.Min(DefaultReviewAtCeiling, autoAccept);
        ValidateThresholds(autoAccept, resolved);
        return new PolicyThresholds(autoAccept, resolved);
    }

    /// <summary>Truncated input and incomplete context: never allows auto, only stronger actions.</summary>
    public static PolicyAction RequireCompleteContext(PolicyAction action, bool truncated)
    {
        return truncated && action == PolicyAction.Auto ? PolicyAction.Review : action;
    }

    /// <summary>Weighted composite in 0..1 from 0..2 rubrics, inverting test gap and blast radius.</summary>
    public static double ReviewComposite(ReviewScores scores)
    {
        var correctness = Clamp01(Clamp02(scores.Correctness) / 2);
        var specMatch = Clamp01(Clamp02(scores.SpecMatch) / 2);
        var tests = Clamp01(1 - (Clamp02(scores.TestGap) / 2));
        var blast = Clamp01(1 - (Clamp02(scores.BlastRadius) / 2));

        return (ReviewWeights.Correctness * correctness) +
            (ReviewWeights.SpecMatch * specMatch) +
            (ReviewWeights.TestGap * tests) +
            (ReviewWeights.BlastRadius * blast);
    }

    /// <summary>
    /// Patch-review action. Escalates when score confidence or safe_to_apply is unknown
    /// or below review_at; auto only when safe_to_apply and the lowest confidence meet
    /// auto_accept and the composite clears the floor; otherwise review.
    /// </summary>
    public static PolicyAction ReviewAction(
        double composite,
        double safeToApply,
        double? minConfidence,
        double autoAccept,
        double reviewAt,
        double compositeFloor)
    {
        if (minConfidence is not { } confidence || confidence < reviewAt || safeToApply < reviewAt)
        {
            return PolicyAction.Escalate;
        }

        if (safeToApply >= autoAccept && composite >= compositeFloor && confidence >= autoAccept)
        {
            return PolicyAction.Auto;
        }

        return PolicyAction.Review;
    }

    /// <summary>
    /// Per-claim action: unknown or low confidence and a confident contradiction escalate;
    /// only a confident verification is auto. Unknown confidence never satisfies a threshold,
    /// even when the threshold is zero.
    /// </summary>
    public static PolicyAction ClaimAction(
        ClaimVerdict verdict,
        double? confidence,
        double autoAccept,
        double reviewAt)
    {
        if (confidence is not { } value || value < reviewAt)
        {
            return PolicyAction.Escalate;
        }

        if (verdict == ClaimVerdict.Contradicted && value >= autoAccept)
        {
            return PolicyAction.Escalate;
        }

        return verdict == ClaimVerdict.Verified && value >= autoAccept ? PolicyAction.Auto : PolicyAction.Review;
    }

    /// <summary>The most severe action wins: a gate is worth its weakest judgment.</summary>
    public static PolicyAction WorstAction(IEnumerable<PolicyAction> actions)
    {
        var worst = PolicyAction.Auto;
        foreach (var action in actions)
        {
            if (action > worst)
            {
                worst = action;
            }
        }

        return worst;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static double Clamp02(double value) => Math.Clamp(value, 0, 2);

    private static string Fixed2(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Plain(double value) => value.ToString(CultureInfo.InvariantCulture);
}
