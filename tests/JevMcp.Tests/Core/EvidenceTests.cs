using JevMcp.Core;

namespace JevMcp.Tests;

public sealed class EvidenceTests
{
    [Fact]
    public void ASingleTextBecomesAnItemWithAFixedId()
    {
        var items = Evidence.Normalize("just text");

        Assert.Equal(new[] { new IdentifiedItem("evidence", "just text") }, items);
    }

    [Fact]
    public void ItemWithoutIdGetsAnIndexedFallback()
    {
        var items = Evidence.Normalize([new TextItem(null, "single")]);

        Assert.Equal(new[] { new IdentifiedItem("evidence0", "single") }, items);
    }

    [Fact]
    public void DuplicateIdsAreDisambiguated()
    {
        var items = Evidence.Normalize([new TextItem("a", "one"), new TextItem("a", "two")]);

        Assert.Equal(new[] { "a", "a_1" }, items.Select(item => item.Id));
        Assert.True(Evidence.HasNonEmpty(items));
    }

    [Fact]
    public void WhitespaceOnlyEvidenceDoesNotCount()
    {
        Assert.False(Evidence.HasNonEmpty([new IdentifiedItem("x", " \n ")]));
        Assert.False(Evidence.HasNonEmpty([]));
    }
}
