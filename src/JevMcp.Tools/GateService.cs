using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JevMcp.Core;
using JevMcp.Providers;
using static System.FormattableString;

namespace JevMcp.Tools;

/// <summary>A patch and the completion claims that come with it, plus their evidence.</summary>
public sealed record GateRequest(
    string Request,
    string Diff,
    IReadOnlyList<string> Claims,
    IReadOnlyList<TextItem> Evidence,
    string? Tests = null,
    double? AutoAccept = null,
    double? ReviewAt = null,
    double? CompositeFloor = null);

public sealed record GateClaimResult([property: JsonIgnore] PolicyAction Decision)
{
    public required string Claim { get; init; }

    public string? Verdict { get; init; }

    public double? Confidence { get; init; }

    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    public string Action => JevWireNames.Of(Decision);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
}

public sealed record GateVerificationSummary(
    int Verified,
    int Contradicted,
    int Unsupported,
    int NeedsReview,
    int InvalidResponse);

public sealed record GateThresholds(double AutoAccept, double ReviewAt);

public sealed record GateVerification([property: JsonIgnore] PolicyAction Decision)
{
    public string Action => JevWireNames.Of(Decision);

    public required GateVerificationSummary Summary { get; init; }

    public required GateThresholds Thresholds { get; init; }

    public required IReadOnlyList<GateClaimResult> Results { get; init; }
}

public sealed record GateResult
{
    public string Tool => JevTools.Gate;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public required bool Truncated { get; init; }

    public required string Action { get; init; }

    public required IReadOnlyList<string> ReasonCodes { get; init; }

    public required ReviewHalf Review { get; init; }

    public required GateVerification Verification { get; init; }

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// Completion gate: the same review as <c>jev_review</c> plus verification of the claims
/// against the evidence, in a single call. Auto-accepts only when the review passes and
/// every claim is verified above the threshold.
/// </summary>
public sealed class GateService
{
    private const string ClaimFraming =
        " Claims are assertions to check, not evidence that the patch is correct or tested.";

    private static readonly ChoiceCriterion[] ClaimCriteria =
        [.. Criteria.VerifyClaimCriteria.Select(entry => new ChoiceCriterion(entry.Key, entry.Value))];

    private readonly IJevClient _client;

    public GateService(IJevClient client)
    {
        _client = client;
    }

    public async Task<GateResult> JudgeAsync(GateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Request))
        {
            throw new ArgumentException("request must not be empty.", nameof(request));
        }

        if (string.IsNullOrEmpty(request.Diff))
        {
            throw new ArgumentException("diff must not be empty.", nameof(request));
        }

        if (request.Claims.Count == 0)
        {
            throw new ArgumentException("claims must contain at least one claim.", nameof(request));
        }

        if (request.Claims.Count > Limits.MaxGateClaims)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"claims must contain at most {Limits.MaxGateClaims} claims.");
        }

        var evidence = Evidence.Normalize(request.Evidence);

        // The gate judges claims against evidence: without evidence there is nothing to
        // judge, and an "unsupported" answer for everything would be expensive noise.
        if (!Evidence.HasNonEmpty(evidence))
        {
            throw new ArgumentException(
                "jev_gate requires at least one evidence item with non-empty text.",
                nameof(request));
        }

        if (evidence.Count > Limits.MaxGateEvidenceItems)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"evidence exceeds {Limits.MaxGateEvidenceItems} items; split the gate or trim the evidence.");
        }

        var evidenceChars = evidence.Sum(item => item.Text.Length);
        if (evidenceChars > Limits.MaxGateEvidenceChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                Invariant($"evidence exceeds the {Limits.MaxGateEvidenceChars:N0}-character aggregate budget; ") +
                "split the gate or trim the evidence.");
        }

        var thresholds = ReviewProjection.ResolveThresholds(
            request.AutoAccept,
            request.ReviewAt,
            request.CompositeFloor,
            nameof(request));

        var truncated =
            request.Request.Length > Limits.MaxReviewDocumentChars ||
            request.Diff.Length > Limits.MaxReviewDocumentChars ||
            (request.Tests?.Length ?? 0) > Limits.MaxReviewDocumentChars ||
            request.Claims.Any(claim => claim.Length > Limits.MaxClaimChars) ||
            evidence.Any(item => item.Text.Length > Limits.MaxReviewDocumentChars);

        var claims = request.Claims
            .Select(claim => Excerpts.Truncate(claim, Limits.MaxClaimChars))
            .ToArray();

        var questions = ReviewProjection.Questions(ClaimFraming);
        for (var index = 0; index < claims.Length; index++)
        {
            // A claim can only be judged from the evidence: diff and tests belong to
            // the review, and the request itself is an assertion, not proof.
            questions[$"claim_{index}"] = new ChoiceQuestion(
                $"Does the evidence support claims[{index}]? Judge only from the provided evidence, not world " +
                "knowledge. Use only the evidence field as factual support; request and claims are assertions, " +
                "not evidence; diff and tests belong to the separate patch review. If a claim needs a diff or " +
                "test log as support, it must be supplied in evidence." + ReviewProjection.AntiInjection,
                ClaimCriteria);
        }

        var claimState = new JsonArray();
        foreach (var claim in claims)
        {
            claimState.Add(claim);
        }

        var evidenceState = new JsonArray();
        foreach (var item in evidence)
        {
            evidenceState.Add(new JsonObject
            {
                ["id"] = item.Id,
                ["text"] = Excerpts.Truncate(item.Text, Limits.MaxReviewDocumentChars),
            });
        }

        var state = new JsonObject
        {
            ["purpose"] = "Review the proposed diff against the request, then check each completion claim " +
                "against the evidence only.",
            ["request"] = Excerpts.Truncate(request.Request, Limits.MaxReviewDocumentChars),
            ["diff"] = Excerpts.Truncate(request.Diff, Limits.MaxReviewDocumentChars),
            ["tests"] = request.Tests is { Length: > 0 } tests
                ? Excerpts.Truncate(tests, Limits.MaxReviewDocumentChars)
                : null,
            ["claims"] = claimState,
            ["evidence"] = evidenceState,
        };

        var answer = await _client.AskAsync(state, questions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var review = ReviewProjection.Project(answer.Answers, thresholds, truncated);
        var expectedKeys = Criteria.VerifyClaimCriteria.Keys.ToArray();

        var results = claims
            .Select((claim, index) => Judge(
                claim,
                answer.Answers.GetValueOrDefault($"claim_{index}"),
                expectedKeys,
                thresholds,
                truncated))
            .ToArray();

        var verification = new GateVerification(Policy.WorstAction(results.Select(result => result.Decision)))
        {
            Summary = new GateVerificationSummary(
                Verified: results.Count(result => result.Verdict == "verified"),
                Contradicted: results.Count(result => result.Verdict == "contradicted"),
                Unsupported: results.Count(result => result.Verdict == "unsupported"),
                NeedsReview: results.Count(result => result.Decision != PolicyAction.Auto),
                InvalidResponse: results.Count(result => result.Status is not null)),
            Thresholds = new GateThresholds(thresholds.AutoAccept, thresholds.ReviewAt),
            Results = results,
        };

        // The gate is worth its weakest judgment: the worse half decides the action.
        var action = Policy.WorstAction([review.Decision, verification.Decision]);

        return new GateResult
        {
            Model = answer.Model,
            Provider = JevWireNames.Of(answer.Provider),
            Truncated = truncated,
            Action = JevWireNames.Of(action),
            ReasonCodes = ReasonCodes(action, truncated, review, verification, results, thresholds),
            Review = review,
            Verification = verification,
            Usage = answer.Usage,
        };
    }

    private static GateClaimResult Judge(
        string claim,
        JevAnswer? raw,
        string[] expectedKeys,
        ReviewThresholds thresholds,
        bool truncated)
    {
        var choice = Answers.ValidateChoice(raw, expectedKeys);

        if (choice is null)
        {
            return new GateClaimResult(PolicyAction.Escalate)
            {
                Claim = claim,
                Verdict = null,
                Confidence = null,
                Probabilities = null,
                Status = JevWireNames.InvalidResponse,
            };
        }

        var verdict = Enum.Parse<ClaimVerdict>(choice.Choice, ignoreCase: true);
        var action = Policy.RequireCompleteContext(
            Policy.ClaimAction(verdict, choice.Confidence, thresholds.AutoAccept, thresholds.ReviewAt),
            truncated);

        return new GateClaimResult(action)
        {
            Claim = claim,
            Verdict = choice.Choice,
            Confidence = choice.Confidence,
            Probabilities = choice.Probabilities,
        };
    }

    /// <summary>
    /// Reason codes: the operator needs to know why the gate stopped, and an action
    /// alone does not say whether it was the review, a contradicted claim, or low confidence.
    /// </summary>
    private static IReadOnlyList<string> ReasonCodes(
        PolicyAction action,
        bool truncated,
        ReviewHalf review,
        GateVerification verification,
        IReadOnlyList<GateClaimResult> results,
        ReviewThresholds thresholds)
    {
        var codes = new List<string>();

        if (truncated)
        {
            codes.Add("incomplete_context");
        }

        if (review.Status is not null || verification.Summary.InvalidResponse > 0)
        {
            codes.Add(JevWireNames.InvalidResponse);
        }

        if (review.Decision == PolicyAction.Escalate)
        {
            codes.Add("review_escalated");
        }

        if (review.Decision == PolicyAction.Review)
        {
            codes.Add("review_required");
        }

        if (verification.Summary.Contradicted > 0)
        {
            codes.Add("claims_contradicted");
        }

        if (verification.Summary.Unsupported > 0)
        {
            codes.Add("claims_unsupported");
        }

        // Missing confidence counts as below everything: it satisfies no threshold.
        var confidences = results
            .Where(result => result.Status is null)
            .Select(result => result.Confidence ?? -1)
            .ToArray();

        if (confidences.Any(confidence => confidence < thresholds.ReviewAt))
        {
            codes.Add("claim_confidence_low");
        }

        if (confidences.Any(confidence =>
                confidence >= thresholds.ReviewAt && confidence < thresholds.AutoAccept))
        {
            codes.Add("claim_confidence_below_auto_accept");
        }

        if (action == PolicyAction.Auto)
        {
            codes.Add("accepted");
        }

        return codes;
    }
}
