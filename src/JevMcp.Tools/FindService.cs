using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JevMcp.Core;
using JevMcp.Providers;

namespace JevMcp.Tools;

/// <summary>Semantic search over candidates, without embeddings.</summary>
public sealed record FindRequest(string Query, IReadOnlyList<TextItem> Candidates, int? TopK = null);

public sealed record FindHit(string Id, double Probability, string Text);

public sealed record FindResult
{
    public string Tool => JevTools.Find;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public required string Query { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    public double? Exists { get; init; }

    public string? ExistsVerdict { get; init; }

    public required IReadOnlyList<FindHit> Top { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// One Choice scores every candidate and a separate Noul says whether any of them answers
/// the query — without that Noul, the top hit would pass itself off as an answer.
/// </summary>
public sealed class FindService
{
    /// <summary>Default number of candidates returned.</summary>
    public const int DefaultTopK = 5;

    /// <summary>Cap of candidates returned per call.</summary>
    public const int MaxTopK = 50;

    private readonly IJevClient _client;

    public FindService(IJevClient client)
    {
        _client = client;
    }

    public async Task<FindResult> JudgeAsync(FindRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Query))
        {
            throw new ArgumentException("query must not be empty.", nameof(request));
        }

        if (request.Candidates.Count == 0)
        {
            throw new ArgumentException("candidates must contain at least one candidate.", nameof(request));
        }

        // The limit is checked before any call to the model: exceeding a Choice's option
        // cap would be an API error, and paying for that helps nobody.
        if (request.Candidates.Count > Limits.MaxCandidates)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"candidates must contain at most {Limits.MaxCandidates} candidates.");
        }

        var topK = request.TopK ?? DefaultTopK;
        if (topK < 1 || topK > MaxTopK)
        {
            throw new ArgumentOutOfRangeException(nameof(request), $"top_k must be between 1 and {MaxTopK}.");
        }

        var candidates = Identifiers.EnsureUniqueIds(
            request.Candidates.Select(candidate => candidate with
            {
                Text = Excerpts.Truncate(candidate.Text, Limits.MaxCandidateChars),
            }),
            "candidate").Items;

        var questions = new OrderedDictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["best"] = new ChoiceQuestion(
                $"Which candidate contains the best answer to: \"{request.Query}\"?",
                [.. candidates.Select(candidate => new ChoiceCriterion(candidate.Id))]),
            ["exists"] = new NoulQuestion(
                $"Does any candidate address or answer: \"{request.Query}\"?",
                "At least one candidate states or directly implies the answer",
                "No candidate addresses this"),
        };

        var state = new JsonObject
        {
            ["query"] = request.Query,
            ["candidates"] = JevState.ItemsOf(candidates),
        };

        var answer = await _client.AskAsync(state, questions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exists = Answers.ValidateNoul(answer.Answers.GetValueOrDefault("exists"));
        var best = Answers.ValidateChoice(
            answer.Answers.GetValueOrDefault("best"),
            candidates.Select(candidate => candidate.Id));

        if (exists is null || best is null)
        {
            // Absence of exists is not "I did not find it", and absence of best is not
            // "no ranking": both are protocol failures and show up as such.
            return new FindResult
            {
                Model = answer.Model,
                Provider = JevWireNames.Of(answer.Provider),
                Query = request.Query,
                Status = JevWireNames.InvalidResponse,
                Exists = exists,
                ExistsVerdict = null,
                Top = [],
                Reason = "missing or malformed best or exists answer; cannot rank safely",
                Usage = answer.Usage,
            };
        }

        var ranked = Ranking.RankCandidates(candidates, best.Probabilities)
            .Take(topK)
            .Select(hit => new FindHit(hit.Item.Id, Math.Round(hit.Probability, 4), hit.Item.Text))
            .ToArray();

        return new FindResult
        {
            Model = answer.Model,
            Provider = JevWireNames.Of(answer.Provider),
            Query = request.Query,
            Exists = exists,
            ExistsVerdict = JevWireNames.Of(Policy.Exists(exists.Value)),
            Top = ranked,
            Usage = answer.Usage,
        };
    }
}
