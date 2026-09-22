using JevMcp.Core;

namespace JevMcp.Tests;

public sealed class IdentifiersTests
{
    [Theory]
    [InlineData("src/lib.ts", "src_lib.ts")]
    [InlineData("note: hello?!", "note_hello")]
    [InlineData("???", "")]
    [InlineData("already-safe.id_1", "already-safe.id_1")]
    public void SanitizeKeepsSafeCharactersAndCollapsesTheRest(string raw, string expected)
    {
        Assert.Equal(expected, Identifiers.SanitizeId(raw));
    }

    [Fact]
    public void SanitizeCutsAtSixtyFourCharacters()
    {
        Assert.Equal(64, Identifiers.SanitizeId(new string('a', 100)).Length);
    }

    [Fact]
    public void EnsureUniqueIdsUsesIndexedFallbackAndResolvesCollisions()
    {
        var result = Identifiers.EnsureUniqueIds(
            [
                new TextItem("src/lib.ts", "a"),
                new TextItem("src/lib.ts", "b"),
                new TextItem(null, "c"),
            ],
            "candidate");

        Assert.Equal(new[] { "src_lib.ts", "src_lib.ts_1", "candidate2" }, result.Items.Select(item => item.Id));
        Assert.Equal(new[] { "a", "b", "c" }, result.Items.Select(item => item.Text));
        Assert.Single(result.Renamed);
    }

    [Fact]
    public void EnsureUniqueIdsDoesNotRecordARenameWhenTheIdSurvivesIntact()
    {
        var result = Identifiers.EnsureUniqueIds([new TextItem("doc.md", "a")], "candidate");

        Assert.Equal("doc.md", result.Items[0].Id);
        Assert.Empty(result.Renamed);
    }
}
