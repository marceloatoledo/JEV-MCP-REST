namespace JevMcp.Core;

/// <summary>Candidate with the Choice probability assigned by the model.</summary>
public sealed record RankedCandidate(IdentifiedItem Item, double Probability);

/// <summary>Candidate with the relevance score assigned by the model.</summary>
public sealed record ScoredCandidate(IdentifiedItem Item, double Relevance);

/// <summary>Candidate orderings. Ties keep the caller's order.</summary>
public static class Ranking
{
    /// <summary>Orders candidates by Choice probability, descending.</summary>
    public static IReadOnlyList<RankedCandidate> RankCandidates(
        IEnumerable<IdentifiedItem> candidates,
        IReadOnlyDictionary<string, double> probabilities)
    {
        return candidates
            .Select(candidate => new RankedCandidate(
                candidate,
                probabilities.TryGetValue(candidate.Id, out var probability) ? probability : 0))
            .OrderByDescending(ranked => ranked.Probability)
            .ToArray();
    }

    /// <summary>
    /// Orders candidates by index-aligned relevance scores, descending.
    /// Scores are validated before they reach here; the zero fallback only covers an
    /// internal wiring error, never a model answer.
    /// </summary>
    public static IReadOnlyList<ScoredCandidate> RerankByScore(
        IReadOnlyList<IdentifiedItem> candidates,
        IReadOnlyList<double> scores)
    {
        return candidates
            .Select((candidate, index) => new ScoredCandidate(
                candidate,
                index < scores.Count ? scores[index] : 0))
            .OrderByDescending(scored => scored.Relevance)
            .ToArray();
    }
}
