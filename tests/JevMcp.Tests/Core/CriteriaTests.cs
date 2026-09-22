using JevMcp.Core;

namespace JevMcp.Tests;

public sealed class CriteriaTests
{
    [Fact]
    public void EvidenceRelationMapsToTheVerdict()
    {
        Assert.Equal(ClaimVerdict.Verified, Criteria.RelationToVerdict["supports"]);
        Assert.Equal(ClaimVerdict.Contradicted, Criteria.RelationToVerdict["contradicts"]);
        Assert.Equal(ClaimVerdict.Unsupported, Criteria.RelationToVerdict["says_nothing"]);
        Assert.False(Criteria.RelationToVerdict.ContainsKey("other"));
    }

    [Fact]
    public void OverallAndPerAspectComparisonShareTheThreeKeys()
    {
        var overall = Criteria.CompareRelations.Keys.Order().ToArray();
        var aspect = Criteria.AspectRelations.Keys.Order().ToArray();

        Assert.Equal(new[] { "contradicts", "different_facts", "same_fact" }, overall);
        Assert.Equal(overall, aspect);
    }

    [Fact]
    public void AspectExplainsWhatNotCoveringTheTopicMeans()
    {
        var text = Criteria.AspectRelations["different_facts"];

        Assert.True(
            text.Contains("does not both", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("at least one", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DecideOffersExactlyThreeEscapeExits()
    {
        Assert.Equal(3, Criteria.DecideEscapeHatches.Count);
        Assert.Equal(new[] { "ask_user", "investigate", "none" }, Criteria.DecideEscapeHatches.Keys);
    }

    [Fact]
    public void CriterionInsertionOrderIsPreserved()
    {
        // Order defines option indexes in the question; reordering changes the question.
        Assert.Equal(
            new[] { "same_fact", "contradicts", "different_facts" },
            Criteria.CompareRelations.Keys);
        Assert.Equal(
            new[] { "verified", "contradicted", "unsupported" },
            Criteria.VerifyClaimCriteria.Keys);
        Assert.Equal(
            new[] { "supports", "contradicts", "says_nothing" },
            Criteria.RelationToVerdict.Keys);
    }
}
