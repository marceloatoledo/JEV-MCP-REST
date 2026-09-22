using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JevMcp.Core;
using JevMcp.Providers;
using static System.FormattableString;

namespace JevMcp.Tools;

/// <summary>Candidates to rerank against a query.</summary>
public sealed record RerankRequest(string Query, IReadOnlyList<TextItem> Candidates, int? TopK = null);

public sealed record RerankHit(int Rank, string Id, double Relevance, string Text);

public sealed record RerankSummary(int Candidates, int Returned);

public sealed record RerankResult
{
    public string Tool => JevTools.Rerank;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public required string Query { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RerankSummary? Summary { get; init; }

    public IReadOnlyList<RerankHit>? Ranked { get; init; }

    /// <summary>auto when every candidate scored; escalate on invalid_response.</summary>
    public required string Action { get; init; }

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// Reranking: one relevance Noul per candidate, all in the same request. Unlike
/// <c>jev_find</c>, which elects a winner, here the entire list survives.
/// </summary>
public sealed class RerankService
{
    /// <summary>Character limit of the query.</summary>
    public const int MaxQueryChars = 2_000;

    private readonly IJevClient _client;

    public RerankService(IJevClient client)
    {
        _client = client;
    }

    public async Task<RerankResult> JudgeAsync(RerankRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Query))
        {
            throw new ArgumentException("query must not be empty.", nameof(request));
        }

        if (request.Query.Length > MaxQueryChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                Invariant($"query must be at most {MaxQueryChars:N0} characters."));
        }

        if (request.Candidates.Count == 0)
        {
            throw new ArgumentException("candidates must contain at least one candidate.", nameof(request));
        }

        if (request.Candidates.Count > Limits.MaxRerankCandidates)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"candidates must contain at most {Limits.MaxRerankCandidates} candidates.");
        }

        var topK = request.TopK;
        if (topK is { } requested && (requested < 1 || requested > Limits.MaxRerankCandidates))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"top_k must be between 1 and {Limits.MaxRerankCandidates}.");
        }

        var candidates = Keyed.Map(
            request.Candidates,
            "candidate",
            "c",
            candidate => candidate.Id,
            candidate => Excerpts.Truncate(candidate.Text, Limits.MaxCandidateChars));

        var totalChars = candidates.Sum(candidate => candidate.Text.Length);
        if (totalChars > Limits.MaxRerankTotalChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                Invariant($"Batch too large: {totalChars:N0} candidate characters exceeds the ") +
                Invariant($"{Limits.MaxRerankTotalChars:N0} character budget. Split the batch."));
        }

        // The query goes once in the state and each question carries only its candidate:
        // the request grows with candidates, not with query-candidate pairs.
        var questions = new OrderedDictionary<string, JevQuestion>(StringComparer.Ordinal);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            questions[$"rel_{index}"] = new NoulQuestion(
                $"Is candidate {candidate.Key} relevant to the query in the state? " +
                $"Candidate {candidate.Key}: {candidate.Text}",
                "The candidate addresses the subject the query asks about, or provides what it seeks",
                "The candidate is about a different subject, or only shares vocabulary with the query");
        }

        var state = new JsonObject { ["query"] = request.Query };

        var answer = await _client.AskAsync(state, questions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var scores = new double[candidates.Count];
        for (var index = 0; index < candidates.Count; index++)
        {
            var score = Answers.ValidateNoul(answer.Answers.GetValueOrDefault($"rel_{index}"));

            // An invalid score contaminates the entire order: a missing answer ranked
            // as zero would be indistinguishable from an irrelevant candidate.
            if (score is null)
            {
                return new RerankResult
                {
                    Model = answer.Model,
                    Provider = JevWireNames.Of(answer.Provider),
                    Query = request.Query,
                    Status = JevWireNames.InvalidResponse,
                    Ranked = null,
                    Action = JevWireNames.Of(Policy.RerankAction(invalidResponse: true)),
                    Usage = answer.Usage,
                };
            }

            scores[index] = score.Value;
        }

        var ranked = Ranking.RerankByScore(
            [.. candidates.Select(candidate => new IdentifiedItem(candidate.External, candidate.Text))],
            scores);

        var returned = (topK is { } take ? ranked.Take(take) : ranked)
            .Select((scored, index) => new RerankHit(
                index + 1,
                scored.Item.Id,
                Math.Round(scored.Relevance, 4),
                scored.Item.Text))
            .ToArray();

        return new RerankResult
        {
            Model = answer.Model,
            Provider = JevWireNames.Of(answer.Provider),
            Query = request.Query,
            Summary = new RerankSummary(candidates.Count, returned.Length),
            Ranked = returned,
            Action = JevWireNames.Of(Policy.RerankAction(invalidResponse: false)),
            Usage = answer.Usage,
        };
    }
}
