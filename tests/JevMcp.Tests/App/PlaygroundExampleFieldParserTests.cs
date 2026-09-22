using JevMcp.App;

namespace JevMcp.Tests.App;

public sealed class PlaygroundExampleFieldParserTests
{
    [Fact]
    public void ParseSplitsFieldLines()
    {
        var fields = PlaygroundExampleFieldParser.Parse("""
            document|Full text.
            auto_accept|Threshold.
            """);

        Assert.Equal(2, fields.Count);
        Assert.Equal(("document", "Full text."), fields[0]);
        Assert.Equal(("auto_accept", "Threshold."), fields[1]);
    }
}
