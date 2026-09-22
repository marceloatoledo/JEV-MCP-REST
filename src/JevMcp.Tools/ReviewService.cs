using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JevMcp.Core;
using JevMcp.Providers;

namespace JevMcp.Tools;

/// <summary>A proposed patch to review against the user's request.</summary>
public sealed record ReviewRequest(
    string Request,
    string Diff,
    string? Tests = null,
    double? AutoAccept = null,
    double? ReviewAt = null,
    double? CompositeFloor = null);

/// <summary>
/// The review is flat in the payload, as in the original: consumers of <c>jev_review</c>
/// read `action` and `composite` at the root. The gate is what nests the same half under `review`.
/// </summary>
public sealed record ReviewResult([property: JsonIgnore] ReviewHalf Half)
{
    public string Tool => JevTools.Review;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public required bool Truncated { get; init; }

    public string Action => Half.Action;

    public double? Composite => Half.Composite;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status => Half.Status;

    public double? SafeToApply => Half.SafeToApply;

    public IReadOnlyDictionary<string, ReviewScore> Scores => Half.Scores;

    public IReadOnlyDictionary<string, double> Weights => Half.Weights;

    public ReviewThresholds Thresholds => Half.Thresholds;

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// Patch review by rubrics: four 0..2 scores and a probability that it is safe to apply,
/// combined into a weighted composite and a single action. Does not run tests or apply
/// the patch.
/// </summary>
public sealed class ReviewService
{
    private readonly IJevClient _client;

    public ReviewService(IJevClient client)
    {
        _client = client;
    }

    public async Task<ReviewResult> JudgeAsync(ReviewRequest request, CancellationToken cancellationToken = default)
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

        var thresholds = ReviewProjection.ResolveThresholds(
            request.AutoAccept,
            request.ReviewAt,
            request.CompositeFloor,
            nameof(request));

        // A document above the cap is truncated, not rejected, because a large diff can
        // still be reviewed; what it must not be is auto-accepted.
        var truncated =
            request.Request.Length > Limits.MaxReviewDocumentChars ||
            request.Diff.Length > Limits.MaxReviewDocumentChars ||
            (request.Tests?.Length ?? 0) > Limits.MaxReviewDocumentChars;

        var state = new JsonObject
        {
            ["purpose"] = "Review the proposed diff against the request; tests is reported test output.",
            ["request"] = Excerpts.Truncate(request.Request, Limits.MaxReviewDocumentChars),
            ["diff"] = Excerpts.Truncate(request.Diff, Limits.MaxReviewDocumentChars),
            ["tests"] = request.Tests is { Length: > 0 } tests
                ? Excerpts.Truncate(tests, Limits.MaxReviewDocumentChars)
                : null,
        };

        var answer = await _client
            .AskAsync(state, ReviewProjection.Questions(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new ReviewResult(ReviewProjection.Project(answer.Answers, thresholds, truncated))
        {
            Model = answer.Model,
            Provider = JevWireNames.Of(answer.Provider),
            Truncated = truncated,
            Usage = answer.Usage,
        };
    }
}
