using JevMcp.Core;

namespace JevMcp.Tests;

public sealed class PolicyTests
{
    [Theory]
    [InlineData(0.8, 0.8, PolicyAction.Auto)]
    [InlineData(0.79, 0.8, PolicyAction.Review)]
    [InlineData(0.99, 0.8, PolicyAction.Auto)]
    public void VerifyActionCutsAtAutoAccept(double confidence, double autoAccept, PolicyAction expected)
    {
        Assert.Equal(expected, Policy.VerifyAction(confidence, autoAccept));
    }

    [Fact]
    public void ScreenEscalatesOnInjectionBeforeDiscardingUselessContent()
    {
        Assert.Equal(ScreenAction.Block, Policy.Screen(0.9, 0.75, 0.25).Action);
        Assert.Equal(ScreenAction.Review, Policy.Screen(0.4, 0.75, 0.25).Action);
        Assert.Equal(ScreenAction.Pass, Policy.Screen(0.01, 0.75, 0.25).Action);
        Assert.Equal(ScreenAction.Skip, Policy.Screen(0.01, 0.75, 0.25, substance: 0.1).Action);
        Assert.Equal(ScreenAction.Skip, Policy.Screen(0.01, 0.75, 0.25, relevance: 0.05).Action);
    }

    [Fact]
    public void ScreenBlocksEvenWhenContentWouldBeSkipped()
    {
        // Injection decides first: dangerous text is blocked, not ignored.
        var recommendation = Policy.Screen(0.9, 0.75, 0.25, relevance: 0.01, substance: 0.01);

        Assert.Equal(ScreenAction.Block, recommendation.Action);
        Assert.Equal("injection probability 0.90 >= block threshold 0.75", recommendation.Reason);
    }

    [Theory]
    [InlineData(0.98, ExistsVerdict.Answered)]
    [InlineData(0.7, ExistsVerdict.Answered)]
    [InlineData(0.46, ExistsVerdict.Partial)]
    [InlineData(0.35, ExistsVerdict.Partial)]
    [InlineData(0.14, ExistsVerdict.Absent)]
    public void ExistsUsesTheCookbookCuts(double exists, ExistsVerdict expected)
    {
        Assert.Equal(expected, Policy.Exists(exists));
    }

    [Fact]
    public void MarginMeasuresDistanceFromWinnerToRunnerUp()
    {
        Assert.Equal(0.5, Policy.Margin(Probabilities(("a", 0.7), ("b", 0.2), ("c", 0.1))), 9);
        Assert.Equal(0d, Policy.Margin(Probabilities(("a", 0.5), ("b", 0.5))));
        Assert.Equal(0d, Policy.Margin(Probabilities(("only", 0.8))));
        Assert.Equal(0d, Policy.Margin(null));
    }

    [Theory]
    [InlineData(0.9, 0.6, PolicyAction.Auto)]
    [InlineData(0.9, 0.4, PolicyAction.Review)]
    [InlineData(0.8, 0.8, PolicyAction.Review)]
    [InlineData(0.85, 0.5, PolicyAction.Auto)]
    public void ClassificationDecisionRequiresTopAndMargin(double top, double margin, PolicyAction expected)
    {
        Assert.Equal(expected, Policy.ClassificationDecision(top, margin, 0.85, 0.5));
    }

    [Fact]
    public void HighTopWithNarrowMarginGoesToReview()
    {
        var margin = Policy.Margin(Probabilities(("a", 0.9), ("b", 0.88)));

        Assert.Equal(PolicyAction.Review, Policy.ClassificationDecision(0.9, margin, 0.85, 0.5));
    }

    [Fact]
    public void ZeroConfidenceIsComparedNormallyAgainstThresholds()
    {
        // Zero is a model answer, not absence: with zeroed thresholds it satisfies accept.
        Assert.Equal(PolicyAction.Auto, Policy.ClaimAction(ClaimVerdict.Verified, 0, 0, 0));
        Assert.Equal(PolicyAction.Escalate, Policy.ClaimAction(ClaimVerdict.Verified, 0, 0.8, 0.5));
    }

    [Fact]
    public void ContradictsRecommendationNamesOnlyTheRecommendedCandidateRequirements()
    {
        RequirementCheck[] checks =
        [
            new("a", 0, RequirementVerdict.Contradicted),
            new("a", 1, RequirementVerdict.Supported),
            new("b", 0, RequirementVerdict.Contradicted),
            new("a", 2, RequirementVerdict.Unknown),
        ];

        Assert.Equal(new[] { 0 }, Policy.ContradictsRecommendation(checks, "a"));
        Assert.Equal(new[] { 0 }, Policy.ContradictsRecommendation(checks, "b"));
        Assert.Empty(Policy.ContradictsRecommendation([], "a"));
    }

    [Fact]
    public void ResolveThresholdsFillsThePairWithoutInverting()
    {
        Assert.Equal(new PolicyThresholds(0.8, 0.5), Policy.ResolveThresholds(0.8));
        Assert.Equal(new PolicyThresholds(0.3, 0.3), Policy.ResolveThresholds(0.3));
        Assert.Equal(new PolicyThresholds(0.9, 0.6), Policy.ResolveThresholds(0.9, 0.6));
    }

    [Theory]
    [InlineData(0.5, 0.8)]
    [InlineData(1.2, null)]
    [InlineData(-0.1, null)]
    [InlineData(double.NaN, null)]
    public void ResolveThresholdsRejectsAnInvalidPair(double autoAccept, double? reviewAt)
    {
        var error = Assert.Throws<ArgumentException>(() => Policy.ResolveThresholds(autoAccept, reviewAt));

        Assert.Contains("0 <= review_at <= auto_accept <= 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewCompositeWeightsAndInvertsTestGapAndBlastRadius()
    {
        var perfect = Policy.ReviewComposite(new ReviewScores(2, 2, 0, 0));
        Assert.Equal(1d, perfect, 9);

        Assert.Equal(0d, Policy.ReviewComposite(new ReviewScores(0, 0, 2, 2)), 9);

        // A score outside the range is clamped, never extrapolated.
        Assert.Equal(perfect, Policy.ReviewComposite(new ReviewScores(9, 3, -1, -5)), 9);

        // Weights: correctness 0.4, spec match 0.3, tests 0.15, radius 0.15.
        Assert.Equal(0.4, Policy.ReviewComposite(new ReviewScores(2, 0, 2, 2)), 9);
    }

    [Theory]
    [InlineData(0.9, 0.95, 0.9, 0.7, PolicyAction.Auto)]
    [InlineData(0.5, 0.95, 0.9, 0.7, PolicyAction.Review)]
    [InlineData(0.75, 0.95, 0.9, 0.8, PolicyAction.Review)]
    [InlineData(0.9, 0.4, 0.9, 0.7, PolicyAction.Escalate)]
    [InlineData(0.9, 0.95, 0.2, 0.7, PolicyAction.Escalate)]
    [InlineData(0.9, 0.6, 0.9, 0.7, PolicyAction.Review)]
    public void ReviewActionRequiresSafetyConfidenceAndCompositeFloor(
        double composite,
        double safeToApply,
        double minConfidence,
        double compositeFloor,
        PolicyAction expected)
    {
        Assert.Equal(expected, Policy.ReviewAction(composite, safeToApply, minConfidence, 0.8, 0.5, compositeFloor));
    }

    [Fact]
    public void ReviewActionEscalatesOnUnknownConfidenceEvenAtZeroThreshold()
    {
        // Coercing absence to zero would satisfy auto_accept 0 and review_at 0.
        Assert.Equal(PolicyAction.Escalate, Policy.ReviewAction(0.9, 0.95, null, 0, 0, 0));
        Assert.Equal(PolicyAction.Escalate, Policy.ReviewAction(0.9, 0.95, null, 0.8, 0.5, 0.7));
    }

    [Theory]
    [InlineData(PolicyAction.Auto, true, PolicyAction.Review)]
    [InlineData(PolicyAction.Auto, false, PolicyAction.Auto)]
    [InlineData(PolicyAction.Escalate, true, PolicyAction.Escalate)]
    [InlineData(PolicyAction.Review, true, PolicyAction.Review)]
    public void RequireCompleteContextDowngradesOnlyAuto(PolicyAction action, bool truncated, PolicyAction expected)
    {
        Assert.Equal(expected, Policy.RequireCompleteContext(action, truncated));
    }

    [Theory]
    [InlineData(ClaimVerdict.Verified, 0.95, PolicyAction.Auto)]
    [InlineData(ClaimVerdict.Verified, 0.6, PolicyAction.Review)]
    [InlineData(ClaimVerdict.Verified, 0.3, PolicyAction.Escalate)]
    [InlineData(ClaimVerdict.Contradicted, 0.95, PolicyAction.Escalate)]
    [InlineData(ClaimVerdict.Contradicted, 0.6, PolicyAction.Review)]
    [InlineData(ClaimVerdict.Unsupported, 0.95, PolicyAction.Review)]
    public void ClaimActionEscalatesLowConfidenceAndConfidentContradiction(
        ClaimVerdict verdict,
        double confidence,
        PolicyAction expected)
    {
        Assert.Equal(expected, Policy.ClaimAction(verdict, confidence, 0.8, 0.5));
    }

    [Fact]
    public void ClaimActionEscalatesOnUnknownConfidenceEvenAtZeroThreshold()
    {
        Assert.Equal(PolicyAction.Escalate, Policy.ClaimAction(ClaimVerdict.Verified, null, 0, 0));
        Assert.Equal(PolicyAction.Escalate, Policy.ClaimAction(ClaimVerdict.Verified, null, 0.8, 0.5));
    }

    [Theory]
    [InlineData(false, PolicyAction.Auto)]
    [InlineData(true, PolicyAction.Escalate)]
    public void RerankActionEscalatesOnlyOnInvalidResponse(bool invalid, PolicyAction expected)
    {
        Assert.Equal(expected, Policy.RerankAction(invalid));
    }

    [Theory]
    [InlineData(false, false, false, 0.9, PolicyAction.Auto)]
    [InlineData(false, true, false, 0.9, PolicyAction.Review)]
    [InlineData(false, false, true, 0.9, PolicyAction.Review)]
    [InlineData(false, false, false, 0.7, PolicyAction.Review)]
    [InlineData(true, false, false, null, PolicyAction.Escalate)]
    public void DecideActionFollowsEscapeWarningsAndConfidence(
        bool invalid,
        bool escaped,
        bool warnings,
        double? confidence,
        PolicyAction expected)
    {
        Assert.Equal(expected, Policy.DecideAction(invalid, escaped, warnings, confidence));
    }

    [Fact]
    public void WorstActionPicksTheMostSevereAction()
    {
        Assert.Equal(PolicyAction.Review, Policy.WorstAction([PolicyAction.Auto, PolicyAction.Review]));
        Assert.Equal(
            PolicyAction.Escalate,
            Policy.WorstAction([PolicyAction.Auto, PolicyAction.Review, PolicyAction.Escalate]));
        Assert.Equal(PolicyAction.Auto, Policy.WorstAction([PolicyAction.Auto]));
        Assert.Equal(PolicyAction.Auto, Policy.WorstAction([]));
    }

    private static Dictionary<string, double> Probabilities(params (string Key, double Value)[] entries)
    {
        return entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }
}
