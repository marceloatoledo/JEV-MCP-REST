using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JevMcp.Core;
using JevMcp.Providers;

namespace JevMcp.Tools;

/// <summary>Claims to verify against the evidence.</summary>
public sealed record VerifyRequest(
    IReadOnlyList<string> Claims,
    IReadOnlyList<TextItem> Evidence,
    double? AutoAccept = null);

/// <summary>Per-claim result. `status` appears only when the model response is unusable.</summary>
public sealed record VerifyClaimResult
{
    public required string Id { get; init; }

    public required string Claim { get; init; }

    public required string Verdict { get; init; }

    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    public double? Confidence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    public required string Action { get; init; }

    public string? SupportingEvidence { get; init; }
}

public sealed record VerifySummary(int Verified, int Contradicted, int Unsupported, int NeedsReview);

public sealed record VerifyResult
{
    public string Tool => JevTools.Verify;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public double AutoAccept { get; init; }

    public required VerifySummary Summary { get; init; }

    public required IReadOnlyList<VerifyClaimResult> Results { get; init; }

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// Claim verification against evidence, following the citation cookbook: one relation
/// Choice per claim and, when there is more than one evidence item, a source Choice.
/// </summary>
public sealed class VerifyService
{
    private const string NoneSource = "none";

    private static readonly ChoiceCriterion[] RelationCriteria =
    [
        new("supports", "The evidence states the claim or directly implies that it is true"),
        new("contradicts", "The evidence states the opposite of the claim or implies that it is false"),
        new("says_nothing", "The evidence does not address what the claim asserts, either way"),
    ];

    private readonly IJevClient _client;

    public VerifyService(IJevClient client)
    {
        _client = client;
    }

    public async Task<VerifyResult> JudgeAsync(VerifyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Claims.Count == 0)
        {
            throw new ArgumentException("claims must contain at least one claim.", nameof(request));
        }

        if (request.Evidence.Count == 0)
        {
            throw new ArgumentException("evidence must contain at least one item.", nameof(request));
        }

        var autoAccept = request.AutoAccept ?? Policy.DefaultAutoAccept;
        if (!double.IsFinite(autoAccept) || autoAccept < 0 || autoAccept > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "auto_accept must be between 0 and 1.");
        }

        var evidence = Identifiers.EnsureUniqueIds(request.Evidence, Evidence.FallbackPrefix).Items;
        var claims = Identifiers
            .EnsureUniqueIds(request.Claims.Select(text => new TextItem(null, text)), "claim")
            .Items;

        var questions = new OrderedDictionary<string, JevQuestion>(StringComparer.Ordinal);
        foreach (var claim in claims)
        {
            questions[$"relation_{claim.Id}"] = new ChoiceQuestion(
                $"How does the evidence relate to claim `{claim.Id}` ({claim.Text})?",
                RelationCriteria);

            if (evidence.Count > 1)
            {
                var sources = new List<ChoiceCriterion>(evidence.Count + 1);
                sources.AddRange(evidence.Select(item => new ChoiceCriterion(item.Id)));
                sources.Add(new ChoiceCriterion(
                    NoneSource,
                    "No single evidence item contains the content the claim depends on"));

                questions[$"source_{claim.Id}"] = new ChoiceQuestion(
                    $"Which evidence item does claim `{claim.Id}` ({claim.Text}) rest on?",
                    sources);
            }
        }

        var state = new JsonObject
        {
            ["purpose"] = "Verify each claim in claims against the evidence in evidence.",
            ["claims"] = JevState.ItemsOf(claims),
            ["evidence"] = JevState.ItemsOf(evidence),
        };

        var answer = await _client.AskAsync(state, questions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var sourceKeys = evidence.Select(item => item.Id).Append(NoneSource).ToArray();
        var relationKeys = Criteria.RelationToVerdict.Keys.ToArray();

        var results = claims
            .Select(claim => Judge(claim, answer, relationKeys, sourceKeys, evidence.Count, autoAccept))
            .ToArray();

        return new VerifyResult
        {
            Model = answer.Model,
            Provider = JevWireNames.Of(answer.Provider),
            AutoAccept = autoAccept,
            Summary = new VerifySummary(
                Verified: results.Count(result => result.Verdict == JevWireNames.Of(ClaimVerdict.Verified)),
                Contradicted: results.Count(result => result.Verdict == JevWireNames.Of(ClaimVerdict.Contradicted)),
                Unsupported: results.Count(result => result.Verdict == JevWireNames.Of(ClaimVerdict.Unsupported)),
                NeedsReview: results.Count(result => result.Action == JevWireNames.Of(PolicyAction.Review))),
            Results = results,
            Usage = answer.Usage,
        };
    }

    private static VerifyClaimResult Judge(
        IdentifiedItem claim,
        JevAskResult answer,
        string[] relationKeys,
        string[] sourceKeys,
        int evidenceCount,
        double autoAccept)
    {
        var raw = answer.Answers.GetValueOrDefault($"relation_{claim.Id}");
        var relation = Answers.ValidateChoice(raw, relationKeys);

        // Broken confidence invalidates the relation; missing confidence only leaves
        // the verdict unbacked and falls through to review.
        var valid = relation is not null && (raw!.Confidence is null || relation.Confidence is not null);

        if (!valid)
        {
            return new VerifyClaimResult
            {
                Id = claim.Id,
                Claim = claim.Text,
                Verdict = JevWireNames.UnknownVerdict,
                Probabilities = null,
                Confidence = null,
                Status = JevWireNames.InvalidResponse,
                Action = JevWireNames.Of(PolicyAction.Review),
                SupportingEvidence = null,
            };
        }

        // The source Choice is auxiliary information, requested only when there are
        // several evidence items. Its absence does not invalidate the verdict.
        var source = evidenceCount > 1
            ? Answers.ValidateChoice(answer.Answers.GetValueOrDefault($"source_{claim.Id}"), sourceKeys)
            : null;

        return new VerifyClaimResult
        {
            Id = claim.Id,
            Claim = claim.Text,
            Verdict = JevWireNames.Of(Criteria.RelationToVerdict[relation!.Choice]),
            Probabilities = relation.Probabilities,
            Confidence = relation.Confidence,
            Action = relation.Confidence is { } confidence
                ? JevWireNames.Of(Policy.VerifyAction(confidence, autoAccept))
                : JevWireNames.Of(PolicyAction.Review),
            SupportingEvidence = source is not null && source.Choice != NoneSource ? source.Choice : null,
        };
    }
}
