using System.Text.Json;
using JevMcp.Providers;
using JevMcp.Tools;
using JevMcp.Tools.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace JevMcp.Tests.Tools;

/// <summary>
/// Tool schemas are a contract with the agent: name, properties, and required fields
/// must match the original jev-mcp, so they are checked here.
/// </summary>
public class McpToolSurfaceTests
{
    private static IReadOnlyDictionary<string, McpServerTool> Tools()
    {
        var services = new ServiceCollection();

        services.AddJevMcpTools(new JevProviderOptions { TypeSafeApiKey = "key" });
        services.AddMcpServer()
            .WithTools<JevVerifyTool>()
            .WithTools<JevScreenTool>()
            .WithTools<JevFindTool>()
            .WithTools<JevClassifyTool>()
            .WithTools<JevDecideTool>()
            .WithTools<JevRerankTool>()
            .WithTools<JevCompareTool>()
            .WithTools<JevExtractTool>()
            .WithTools<JevReviewTool>()
            .WithTools<JevGateTool>();

        return services
            .BuildServiceProvider()
            .GetServices<McpServerTool>()
            .ToDictionary(tool => tool.ProtocolTool.Name, StringComparer.Ordinal);
    }

    private static (JsonElement Properties, IReadOnlyList<string> Required) Schema(McpServerTool tool)
    {
        var schema = tool.ProtocolTool.InputSchema;
        var required = schema.TryGetProperty("required", out var element)
            ? element.EnumerateArray().Select(entry => entry.GetString()!).ToArray()
            : [];

        return (schema.GetProperty("properties"), required);
    }

    [Fact]
    public void AllTenToolsAreRegisteredUnderTheirWireNames()
    {
        Assert.Equal(
            new[]
            {
                JevTools.Classify,
                JevTools.Compare,
                JevTools.Decide,
                JevTools.Extract,
                JevTools.Find,
                JevTools.Gate,
                JevTools.Rerank,
                JevTools.Review,
                JevTools.Screen,
                JevTools.Verify,
            },
            Tools().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void VerifyTakesClaimsAndEvidenceAndAnOptionalSnakeCaseAutoAccept()
    {
        var (properties, required) = Schema(Tools()[JevTools.Verify]);

        Assert.True(properties.TryGetProperty("claims", out _));
        Assert.True(properties.TryGetProperty("evidence", out _));
        Assert.True(properties.TryGetProperty("auto_accept", out _));
        Assert.Equal(new[] { "claims", "evidence" }, required);
    }

    [Fact]
    public void ScreenRequiresOnlyTextAndKeepsTheThresholdNames()
    {
        var (properties, required) = Schema(Tools()[JevTools.Screen]);

        Assert.True(properties.TryGetProperty("purpose", out _));
        Assert.True(properties.TryGetProperty("block_at", out _));
        Assert.True(properties.TryGetProperty("review_at", out _));
        Assert.Equal(new[] { "text" }, required);
    }

    [Fact]
    public void FindRequiresQueryAndCandidatesWithAnOptionalTopK()
    {
        var (properties, required) = Schema(Tools()[JevTools.Find]);

        Assert.True(properties.TryGetProperty("top_k", out _));
        Assert.Equal(new[] { "query", "candidates" }, required);
    }

    [Fact]
    public void ClassifyRequiresItemsAndClassesAndKeepsTheMarginName()
    {
        var (properties, required) = Schema(Tools()[JevTools.Classify]);

        Assert.True(properties.TryGetProperty("minimum_margin", out _));
        Assert.True(properties.TryGetProperty("context", out _));
        Assert.Equal(new[] { "items", "classes" }, required);
    }

    [Fact]
    public void DecideRequiresTheDecisionEvidencePrioritiesAndCandidates()
    {
        var (properties, required) = Schema(Tools()[JevTools.Decide]);

        Assert.True(properties.TryGetProperty("requirements", out _));
        Assert.True(properties.TryGetProperty("escape_hatches", out _));
        Assert.Equal(new[] { "decision", "evidence", "priorities", "candidates" }, required);
    }

    [Fact]
    public void RerankRequiresQueryAndCandidates()
    {
        var (properties, required) = Schema(Tools()[JevTools.Rerank]);

        Assert.True(properties.TryGetProperty("top_k", out _));
        Assert.Equal(new[] { "query", "candidates" }, required);
    }

    [Fact]
    public void CompareRequiresBothPassagesUnderTheirSnakeCaseNames()
    {
        var (properties, required) = Schema(Tools()[JevTools.Compare]);

        Assert.True(properties.TryGetProperty("aspects", out _));
        Assert.Equal(new[] { "passage_a", "passage_b" }, required);
    }

    [Fact]
    public void ExtractRequiresTheDocumentAndTheFields()
    {
        var (_, required) = Schema(Tools()[JevTools.Extract]);

        Assert.Equal(new[] { "document", "fields" }, required);
    }

    [Fact]
    public void ReviewRequiresTheRequestAndTheDiffWithTheThresholdTrio()
    {
        var (properties, required) = Schema(Tools()[JevTools.Review]);

        Assert.True(properties.TryGetProperty("auto_accept", out _));
        Assert.True(properties.TryGetProperty("review_at", out _));
        Assert.True(properties.TryGetProperty("composite_floor", out _));
        Assert.Equal(new[] { "request", "diff" }, required);
    }

    [Fact]
    public void GateRequiresTheReviewInputPlusClaimsAndEvidence()
    {
        var (properties, required) = Schema(Tools()[JevTools.Gate]);

        Assert.True(properties.TryGetProperty("tests", out _));
        Assert.Equal(new[] { "request", "diff", "claims", "evidence" }, required);
    }

    [Fact]
    public void EveryToolCarriesADescriptionForTheAgent()
    {
        foreach (var tool in Tools().Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.ProtocolTool.Description));
        }
    }

    [Fact]
    public void CancellationTokenIsNotPartOfTheToolSchema()
    {
        var (properties, _) = Schema(Tools()[JevTools.Screen]);

        Assert.DoesNotContain(
            properties.EnumerateObject().Select(property => property.Name),
            name => name.Contains("cancellation", StringComparison.OrdinalIgnoreCase));
    }
}
