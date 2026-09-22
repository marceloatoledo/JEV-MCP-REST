using JevMcp.Core;

namespace JevMcp.Tests;

public sealed class RankingTests
{
    private static readonly IdentifiedItem[] Candidates =
    [
        new("a", "1"),
        new("b", "2"),
        new("c", "3"),
    ];

    [Fact]
    public void RankCandidatesOrdersByProbabilityAndKeepsCallerOrderOnTies()
    {
        var ranked = Ranking.RankCandidates(
            Candidates,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["a"] = 0.1, ["b"] = 0.5, ["c"] = 0.1 });

        Assert.Equal(new[] { "b", "a", "c" }, ranked.Select(candidate => candidate.Item.Id));
    }

    [Fact]
    public void RankCandidatesTreatsACandidateWithoutProbabilityAsZero()
    {
        var ranked = Ranking.RankCandidates([new IdentifiedItem("x", "1")], new Dictionary<string, double>());

        Assert.Equal(0d, ranked[0].Probability);
    }

    [Fact]
    public void RerankByScoreOrdersByIndexAlignedRelevance()
    {
        var ranked = Ranking.RerankByScore(Candidates, [0.2, 0.9, 0.5]);

        Assert.Equal(new[] { "b", "c", "a" }, ranked.Select(candidate => candidate.Item.Id));
        Assert.Equal(0.9, ranked[0].Relevance);
    }

    [Fact]
    public void RerankByScoreKeepsOriginalOrderOnTies()
    {
        var ranked = Ranking.RerankByScore(Candidates, [0.5, 0.5, 0.5]);

        Assert.Equal(new[] { "a", "b", "c" }, ranked.Select(candidate => candidate.Item.Id));
    }
}
