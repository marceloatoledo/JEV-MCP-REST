using JevMcp.Core;

namespace JevMcp.Tests;

public sealed class ExcerptsTests
{
    [Fact]
    public void TruncateMarksCutText()
    {
        var truncated = Excerpts.Truncate("abcdef", 3);

        Assert.StartsWith("abc", truncated, StringComparison.Ordinal);
        Assert.EndsWith("truncated]", truncated, StringComparison.Ordinal);
        Assert.True(truncated.Length > 3);
    }

    [Fact]
    public void TruncateReturnsTextIntactAtTheLimit()
    {
        Assert.Equal("abc", Excerpts.Truncate("abc", 3));
    }
}
