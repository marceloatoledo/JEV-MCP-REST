using System.Text.Json;
using JevMcp.Core;
using JevMcp.Providers;
using JevMcp.Tools;
using JevMcp.Tools.Mcp;
using ModelContextProtocol;

namespace JevMcp.Tests.Tools;

/// <summary>
/// The SDK only forwards the message of an McpException; any other exception reaches
/// the client as "an error occurred". These tests pin what must get through.
/// </summary>
public class McpToolErrorTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly IReadOnlyDictionary<string, JevAnswer> NoAnswers =
        new Dictionary<string, JevAnswer>(StringComparer.Ordinal);

    [Fact]
    public async Task InvalidInputReachesTheClientWithoutTheDotNetParameterSuffix()
    {
        var tool = new JevVerifyTool(new VerifyService(new FakeJevClient(NoAnswers)));

        var error = await Assert.ThrowsAsync<McpException>(
            () => tool.VerifyAsync([], Json("\"evidence text\"")));

        Assert.Equal("claims must contain at least one claim.", error.Message);
    }

    [Fact]
    public async Task MalformedEvidenceIsRejectedBeforeAnyModelCall()
    {
        var client = new FakeJevClient(NoAnswers);
        var tool = new JevVerifyTool(new VerifyService(client));

        var error = await Assert.ThrowsAsync<McpException>(
            () => tool.VerifyAsync(["claim"], Json("42")));

        Assert.Contains("evidence must be a string", error.Message, StringComparison.Ordinal);
        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task MissingCredentialsAreReportedInsteadOfAGenericFailure()
    {
        var tool = new JevScreenTool(new ScreenService(
            new ThrowingJevClient(() => new JevConfigurationException("No Jev provider credentials found."))));

        var error = await Assert.ThrowsAsync<McpException>(() => tool.ScreenAsync("text"));

        Assert.Equal("No Jev provider credentials found.", error.Message);
    }

    [Fact]
    public async Task TransportFailuresReachTheClientAlreadyRedacted()
    {
        var tool = new JevFindTool(new FindService(
            new ThrowingJevClient(() => new JevTransportException("typesafe request failed: 500 upstream"))));

        var error = await Assert.ThrowsAsync<McpException>(
            () => tool.FindAsync("q", Json("""[{"text": "a"}]""")));

        Assert.Equal("typesafe request failed: 500 upstream", error.Message);
    }

    [Fact]
    public async Task AStructuredArgumentWithTheWrongShapeIsExplainedByField()
    {
        var client = new FakeJevClient(NoAnswers);
        var tool = new JevExtractTool(new ExtractService(client));

        var error = await Assert.ThrowsAsync<McpException>(() => tool.ExtractAsync(
            "a document",
            Json("""[{"id": "total", "description": "the total"}]""")));

        Assert.Equal("each entry of fields must have a pattern string.", error.Message);
        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task ABudgetOverrunSaysWhichBudgetAndWhatToDo()
    {
        var client = new FakeJevClient(NoAnswers);
        var tool = new JevClassifyTool(new ClassifyService(client));

        var items = string.Join(",", Enumerable.Range(0, 64).Select(index => $$"""{"text": "item {{index}}"}"""));
        var classes = string.Join(
            ",",
            Enumerable.Range(0, 250).Select(index => $$"""{"description": "class {{index}}"}"""));

        var error = await Assert.ThrowsAsync<McpException>(() => tool.ClassifyAsync(
            Json($"[{items}]"),
            Json($"[{classes}]")));

        Assert.Equal(
            "Batch too large: 64 items x 250 classes exceeds the 8,000 item-class budget. Split the batch.",
            error.Message);

        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task UnexpectedFailuresAreNotDisguisedAsProtocolErrors()
    {
        var tool = new JevScreenTool(new ScreenService(
            new ThrowingJevClient(() => new InvalidOperationException("bug"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => tool.ScreenAsync("text"));
    }

    [Fact]
    public async Task ASuccessfulCallReturnsTheSerializedResultAsText()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["injection"] = FakeAnswers.Noul(0.01),
            ["substance"] = FakeAnswers.Noul(0.95),
        });

        var payload = await new JevScreenTool(new ScreenService(client)).ScreenAsync("a paragraph");

        using var document = JsonDocument.Parse(payload);
        Assert.Equal("jev_screen", document.RootElement.GetProperty("tool").GetString());
        Assert.Equal("pass", document.RootElement.GetProperty("recommendation").GetProperty("action").GetString());
    }
}
