using System.Text.Json;
using JevMcp.App;
using JevMcp.Data;
using JevMcp.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JevMcp.Tests.Mcp;

[CollectionDefinition(nameof(McpPlaygroundHttpTests), DisableParallelization = true)]
public sealed class McpPlaygroundHttpTestsCollection : ICollectionFixture<McpPlaygroundAppFactory>;

[Collection(nameof(McpPlaygroundHttpTests))]
public sealed class McpPlaygroundHttpTests : IAsyncLifetime
{
    private static readonly string[] ExpectedTools =
    [
        JevTools.Classify, JevTools.Compare, JevTools.Decide, JevTools.Extract, JevTools.Find,
        JevTools.Gate, JevTools.Rerank, JevTools.Review, JevTools.Screen, JevTools.Verify,
    ];

    private readonly McpPlaygroundAppFactory _factory;
    private McpStreamableHttpClient _mcp = null!;

    public McpPlaygroundHttpTests(McpPlaygroundAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        var client = _factory.CreateClient();
        _mcp = new McpStreamableHttpClient(client, _factory.AccessToken);
        await _mcp.ConnectCursorStyleAsync();
    }

    public async Task DisposeAsync()
    {
        if (_mcp is not null)
        {
            await _mcp.DisposeAsync();
        }
    }

    [Fact]
    public async Task CursorStyleSessionOpensSseStreamAndListsTenTools()
    {
        Assert.False(string.IsNullOrWhiteSpace(_mcp.SessionId));

        var tools = await _mcp.ListToolNamesAsync();
        Assert.Equal(ExpectedTools, tools);
    }

    public static IEnumerable<object[]> PlaygroundTools() =>
        PlaygroundMcpSamples.ToolNames.Select(tool => new object[] { tool });

    [Fact]
    public async Task RerankMcpCallPersistsResultingActionInAudit()
    {
        using var response = await _mcp.CallToolAsync(
            JevTools.Rerank,
            PlaygroundMcpSamples.ArgumentsJson(JevTools.Rerank));
        Assert.False(response.RootElement.TryGetProperty("error", out _));

        var writer = _factory.Services.GetRequiredService<CallLogWriter>();
        using var idle = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await writer.WaitForIdleAsync(idle.Token);

        var db = _factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = db.CreateDbContext();
        var log = await context.CallLogs.AsNoTracking()
            .Where(entry => entry.Tool == JevTools.Rerank)
            .OrderByDescending(entry => entry.Id)
            .FirstAsync();
        Assert.Equal("auto", log.ResultingAction);
    }

    [Theory]
    [MemberData(nameof(PlaygroundTools))]
    public async Task PlaygroundSampleCallableOverMcp(string tool)
    {
        using var response = await _mcp.CallToolAsync(tool, PlaygroundMcpSamples.ArgumentsJson(tool));
        var root = response.RootElement;
        Assert.False(root.TryGetProperty("error", out _), root.GetRawText());

        var result = root.GetProperty("result");
        Assert.True(result.TryGetProperty("content", out var content) && content.GetArrayLength() > 0);

        var text = content[0].GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));

        using var payload = JsonDocument.Parse(text!);
        Assert.False(
            payload.RootElement.TryGetProperty("status", out var status) &&
            string.Equals(status.GetString(), "invalid_response", StringComparison.Ordinal),
            text);
    }
}
