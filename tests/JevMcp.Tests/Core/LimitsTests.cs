using JevMcp.Core;

namespace JevMcp.Tests;

/// <summary>
/// Limits exist to fit Jev's contract; a value outside the range turns
/// a valid request into an API error.
/// </summary>
public sealed class LimitsTests
{
    [Fact]
    public void FindAndClassifyCapsFitAChoiceOptionLimit()
    {
        Assert.True(Limits.MaxCandidates <= 255);
        Assert.True(Limits.MaxClasses <= 255);
        Assert.InRange(Limits.MaxItems, 2, 255);
    }

    [Fact]
    public void DecideCapsStaySensible()
    {
        Assert.InRange(Limits.MaxDecideCandidates, 2, 10);
        Assert.InRange(Limits.MaxRequirements, 0, 10);
    }

    [Fact]
    public void RerankCompareAndExtractCapsStaySensible()
    {
        Assert.InRange(Limits.MaxRerankCandidates, 2, 250);
        Assert.InRange(Limits.MaxRerankTotalChars, 10_000, 250_000);
        Assert.InRange(Limits.MaxCompareAspects, 1, 20);
        Assert.InRange(Limits.MaxExtractFields, 1, 64);
        Assert.InRange(Limits.MaxExtractCandidates, 2, 50);
        Assert.InRange(Limits.MaxExtractCandidateChars, 200, 4_000);
    }

    [Fact]
    public void ReviewAndGateCapsKeepRealDiffsReviewable()
    {
        Assert.True(Limits.MaxReviewDocumentChars >= 50_000);
        Assert.InRange(Limits.MaxClaimChars, 500, 4_000);
        Assert.InRange(Limits.MaxGateClaims, 5, 40);
    }
}
