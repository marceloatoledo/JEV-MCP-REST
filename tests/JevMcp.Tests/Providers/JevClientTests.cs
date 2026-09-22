using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using JevMcp.Core;
using JevMcp.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace JevMcp.Tests;

public sealed class JevClientTests
{
    private static readonly JsonNode State = JsonValue.Create("Fui cobrado duas vezes.");

    private static readonly Dictionary<string, JevQuestion> Questions = new(StringComparer.Ordinal)
    {
        ["urgente"] = new NoulQuestion("Escalar agora?"),
    };

    private const string SuccessBody =
        """
        {
          "model": "jev-1.13.0",
          "answers": { "urgente": { "type": "noul", "noul": 0.82 } },
          "usage": { "input_tokens": 434, "output_tokens": 75 }
        }
        """;

    [Fact]
    public async Task TypeSafePostsToTheFixedRouteWithBearerAndFullBody()
    {
        var handler = new FakeHttpHandler(SuccessBody);
        var client = Client(JevProviderKind.TypeSafe, new JevProviderOptions { TypeSafeApiKey = "ts-key" }, handler);

        var result = await client.AskAsync(State, Questions);

        Assert.Equal("https://api.typesafe.ai/v1/systemone", handler.LastRequest.Url?.ToString());
        Assert.Equal("Bearer ts-key", handler.LastRequest.Headers["Authorization"]);

        using var body = JsonDocument.Parse(handler.LastRequest.Body);
        Assert.Equal("jev-latest", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("Fui cobrado duas vezes.", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("noul", body.RootElement.GetProperty("questions").GetProperty("urgente").GetProperty("type").GetString());

        // The effective model is the resolved version the API returned, not the requested alias.
        Assert.Equal("jev-1.13.0", result.Model);
        Assert.Equal(JevProviderKind.TypeSafe, result.Provider);
        Assert.Equal(new JevUsage(434, 75), result.Usage);
        Assert.Equal(0.82, Answers.ValidateNoul(result.Answers["urgente"]));
    }

    [Fact]
    public async Task TypeSafeHonorsACustomOriginAndKeepsTheRoute()
    {
        var handler = new FakeHttpHandler(SuccessBody);
        var options = new JevProviderOptions
        {
            TypeSafeApiKey = "ts-key",
            TypeSafeBaseUrl = "https://interno.example.test/",
        };

        await Client(JevProviderKind.TypeSafe, options, handler).AskAsync(State, Questions);

        Assert.Equal("https://interno.example.test/v1/systemone", handler.LastRequest.Url?.ToString());
    }

    [Theory]
    [InlineData("jev-latest", "typesafe/jev-1.13")]
    [InlineData("jev-1.12", "typesafe/jev-1.12")]
    [InlineData("typesafe/jev-1.13", "typesafe/jev-1.13")]
    public async Task OpenRouterMapsTheLatestAliasAndPrefixesTheNamespace(string model, string expected)
    {
        var handler = new FakeHttpHandler(SuccessBody);
        var options = new JevProviderOptions { OpenRouterApiKey = "sk-or-key", Model = model };

        var result = await Client(JevProviderKind.OpenRouter, options, handler).AskAsync(State, Questions);

        using var body = JsonDocument.Parse(handler.LastRequest.Body);
        Assert.Equal(expected, body.RootElement.GetProperty("model").GetString());
        Assert.Equal(expected, result.Model);
    }

    [Fact]
    public async Task OpenRouterSendsAttributionHeaders()
    {
        var handler = new FakeHttpHandler(SuccessBody);
        var options = new JevProviderOptions { OpenRouterApiKey = "sk-or-key" };

        await Client(JevProviderKind.OpenRouter, options, handler).AskAsync(State, Questions);

        Assert.Equal("https://openrouter.ai/api/alpha/decisions", handler.LastRequest.Url?.ToString());
        Assert.Equal("https://github.com/jkudish/jev-mcp", handler.LastRequest.Headers["HTTP-Referer"]);
        Assert.Equal("jev-mcp", handler.LastRequest.Headers["X-Title"]);
        Assert.Equal("jev-mcp", handler.LastRequest.Headers["X-OpenRouter-Title"]);
    }

    [Fact]
    public async Task CompatibleUsesTheUrlVerbatimWithoutNormalizingPath()
    {
        var handler = new FakeHttpHandler(SuccessBody);
        var options = new JevProviderOptions
        {
            CompatibleApiKey = "jev-key",
            CompatibleBaseUrl = "https://api.openjev.test/v1/systemone",
            Model = "openjev",
        };

        var result = await Client(JevProviderKind.Compatible, options, handler).AskAsync(State, Questions);

        Assert.Equal("https://api.openjev.test/v1/systemone", handler.LastRequest.Url?.ToString());
        Assert.Equal("jev-1.13.0", result.Model);
    }

    [Fact]
    public async Task CompatibleWithoutAModelInTheEnvelopeReportsTheRequestedModel()
    {
        var handler = new FakeHttpHandler("""{"answers":{"urgente":{"type":"noul","noul":0.5}}}""");
        var options = new JevProviderOptions
        {
            CompatibleApiKey = "jev-key",
            CompatibleBaseUrl = "https://api.openjev.test/v1/systemone",
            Model = "openjev",
        };

        var result = await Client(JevProviderKind.Compatible, options, handler).AskAsync(State, Questions);

        Assert.Equal("openjev", result.Model);
    }

    [Fact]
    public async Task AnEndpointThatEchoesTheRequestDoesNotLeakTheCredential()
    {
        var reflected = """{"received":{"headers":{"authorization":"Bearer jev-super-secreta"}}}""";
        var handler = new FakeHttpHandler(reflected, HttpStatusCode.BadRequest);
        var options = new JevProviderOptions
        {
            CompatibleApiKey = "jev-super-secreta",
            CompatibleBaseUrl = "https://api.openjev.test/v1/systemone",
        };

        var error = await Assert.ThrowsAsync<JevTransportException>(
            () => Client(JevProviderKind.Compatible, options, handler).AskAsync(State, Questions));

        Assert.DoesNotContain("jev-super-secreta", error.Message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", error.Message, StringComparison.Ordinal);
        Assert.StartsWith("Jev-compatible endpoint 400:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALongErrorBodyIsCutAtTwoHundredCharacters()
    {
        var handler = new FakeHttpHandler(new string('x', 5_000), HttpStatusCode.InternalServerError);
        var options = new JevProviderOptions
        {
            CompatibleApiKey = "jev-key",
            CompatibleBaseUrl = "https://api.openjev.test/v1/systemone",
        };

        var error = await Assert.ThrowsAsync<JevTransportException>(
            () => Client(JevProviderKind.Compatible, options, handler).AskAsync(State, Questions));

        Assert.Equal(200, error.Message.Count(character => character == 'x'));
    }

    [Theory]
    [InlineData("jev-latest", "typesafe/jev")]
    [InlineData("jev-1.12", "typesafe/jev-1.12")]
    [InlineData("typesafe/jev", "typesafe/jev")]
    public async Task CloudflareWrapsTheContractAndUnwrapsTheResult(string model, string expectedSlug)
    {
        var handler = new FakeHttpHandler(
            """
            {
              "success": true,
              "result": {
                "state": "Completed",
                "result": {
                  "answers": { "urgente": { "type": "noul", "noul": 0.61 } },
                  "usage": { "input_tokens": 10, "output_tokens": 2 }
                }
              }
            }
            """);

        var options = new JevProviderOptions
        {
            CloudflareApiToken = "cf-token",
            CloudflareAccountId = "conta-123",
            Model = model,
        };

        var result = await Client(JevProviderKind.Cloudflare, options, handler).AskAsync(State, Questions);

        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/conta-123/ai/run",
            handler.LastRequest.Url?.ToString());

        using var body = JsonDocument.Parse(handler.LastRequest.Body);
        Assert.Equal(expectedSlug, body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("input").TryGetProperty("questions", out _));
        Assert.True(body.RootElement.GetProperty("input").TryGetProperty("state", out _));

        Assert.Equal(expectedSlug, result.Model);
        Assert.Equal(new JevUsage(10, 2), result.Usage);
        Assert.Equal(0.61, Answers.ValidateNoul(result.Answers["urgente"]));
    }

    [Fact]
    public async Task CloudflareWithFalseSuccessAndStatus200StillFails()
    {
        var handler = new FakeHttpHandler("""{"success":false,"errors":[{"message":"no capacity"}]}""");
        var options = new JevProviderOptions { CloudflareApiToken = "cf-token", CloudflareAccountId = "conta" };

        var error = await Assert.ThrowsAsync<JevTransportException>(
            () => Client(JevProviderKind.Cloudflare, options, handler).AskAsync(State, Questions));

        Assert.Contains("no capacity", error.Message, StringComparison.Ordinal);
        Assert.StartsWith("Cloudflare AI run 200:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CloudflareInANonCompletedStateFailsCitingTheState()
    {
        var handler = new FakeHttpHandler("""{"success":true,"result":{"state":"Queued"}}""");
        var options = new JevProviderOptions { CloudflareApiToken = "cf-token", CloudflareAccountId = "conta" };

        var error = await Assert.ThrowsAsync<JevTransportException>(
            () => Client(JevProviderKind.Cloudflare, options, handler).AskAsync(State, Questions));

        Assert.StartsWith("Cloudflare AI run state Queued:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VercelSendsTheModelInAHeaderAndNoulAsBoolean()
    {
        var handler = new FakeHttpHandler(
            """
            {
              "answers": {
                "urgente": { "type": "boolean", "probability": 0.73 },
                "bucket": { "type": "choice", "choice": "support", "probabilities": { "support": 0.9, "sales": 0.1 } }
              },
              "usage": { "inputTokens": 12, "outputTokens": 3 },
              "providerMetadata": { "typesafe": { "confidence": { "bucket": 0.91 } } }
            }
            """);

        var options = new JevProviderOptions { AiGatewayApiKey = "gw-key" };
        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["urgente"] = new NoulQuestion("Escalar agora?"),
            ["bucket"] = new ChoiceQuestion("Assunto?", [new ChoiceCriterion("support"), new ChoiceCriterion("sales")]),
        };

        var result = await Client(JevProviderKind.Vercel, options, handler).AskAsync(State, questions);

        Assert.Equal(
            "https://ai-gateway.vercel.sh/v4/ai/evaluation-model",
            handler.LastRequest.Url?.ToString());
        Assert.Equal("typesafe-ai/jev", handler.LastRequest.Headers["ai-model-id"]);
        Assert.Equal("4", handler.LastRequest.Headers["ai-evaluation-model-specification-version"]);

        using var body = JsonDocument.Parse(handler.LastRequest.Body);
        Assert.Equal(
            "boolean",
            body.RootElement.GetProperty("questions").GetProperty("urgente").GetProperty("type").GetString());
        Assert.False(body.RootElement.TryGetProperty("model", out _));

        Assert.Equal(0.73, Answers.ValidateNoul(result.Answers["urgente"]));
        Assert.Equal(new JevUsage(12, 3), result.Usage);
        Assert.Equal("typesafe-ai/jev", result.Model);

        // Choice confidence comes from metadata, not from the response body.
        var bucket = Answers.ValidateChoice(result.Answers["bucket"], ["support", "sales"]);
        Assert.NotNull(bucket);
        Assert.Equal(0.91, bucket.Confidence);
    }

    [Fact]
    public async Task VercelWithoutMetadataLeavesConfidenceUnknown()
    {
        var handler = new FakeHttpHandler(
            """
            {
              "answers": { "bucket": { "type": "choice", "choice": "support", "probabilities": { "support": 1 } } }
            }
            """);

        var options = new JevProviderOptions { AiGatewayApiKey = "gw-key" };
        var result = await Client(JevProviderKind.Vercel, options, handler).AskAsync(State, Questions);

        var bucket = Answers.ValidateChoice(result.Answers["bucket"], ["support"]);
        Assert.NotNull(bucket);
        Assert.Null(bucket.Confidence);
    }

    [Fact]
    public async Task VercelWithItsOwnNamespaceAliasPreservesTheSlug()
    {
        var handler = new FakeHttpHandler("""{"answers":{}}""");
        var options = new JevProviderOptions { AiGatewayApiKey = "gw-key", Model = "typesafe-ai/jev-1.13" };

        var result = await Client(JevProviderKind.Vercel, options, handler).AskAsync(State, Questions);

        Assert.Equal("typesafe-ai/jev-1.13", handler.LastRequest.Headers["ai-model-id"]);
        Assert.Equal("typesafe-ai/jev-1.13", result.Model);
    }

    [Fact]
    public async Task UnconfiguredProviderFailsAsAConfigurationError()
    {
        var services = new ServiceCollection();
        services.AddJevProviders(new JevProviderOptions());

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IJevClient>();

        var error = await Assert.ThrowsAsync<JevConfigurationException>(
            () => client.AskAsync(State, Questions));

        Assert.Contains("No Jev provider credentials found", error.Message, StringComparison.Ordinal);
    }

    private static IJevClient Client(JevProviderKind kind, JevProviderOptions options, FakeHttpHandler handler)
    {
        return JevClients.Create(kind, new HttpClient(handler), options);
    }
}
