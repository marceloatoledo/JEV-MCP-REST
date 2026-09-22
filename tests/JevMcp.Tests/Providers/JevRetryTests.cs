using System.Net;
using System.Text.Json.Nodes;
using JevMcp.Core;
using JevMcp.Providers;

namespace JevMcp.Tests;

public sealed class JevRetryTests
{
    private static readonly JsonNode State = JsonValue.Create("estado");

    private static readonly Dictionary<string, JevQuestion> Questions = new(StringComparer.Ordinal)
    {
        ["urgente"] = new NoulQuestion("Escalar agora?"),
    };

    private const string SuccessBody = """{"answers":{"urgente":{"type":"noul","noul":0.5}}}""";

    [Theory]
    [InlineData(429)]
    [InlineData(529)]
    public async Task TransientFailureFollowedBySuccessCompletes(int status)
    {
        var handler = new FakeHttpHandler(
        [
            ((HttpStatusCode)status, """{"error":"slow down"}"""),
            (HttpStatusCode.OK, SuccessBody),
        ]);

        var result = await Client(handler, maxAttempts: 3).AskAsync(State, Questions);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(0.5, Answers.ValidateNoul(result.Answers["urgente"]));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(422)]
    public async Task InvalidCredentialAndInvalidBodyAreNotRetried(int status)
    {
        var handler = new FakeHttpHandler("""{"error":"nope"}""", (HttpStatusCode)status);

        await Assert.ThrowsAsync<JevTransportException>(() => Client(handler, maxAttempts: 3).AskAsync(State, Questions));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AttemptCountHonorsTheConfiguredLimit()
    {
        var handler = new FakeHttpHandler("rate limited", (HttpStatusCode)429);

        await Assert.ThrowsAsync<JevTransportException>(() => Client(handler, maxAttempts: 3).AskAsync(State, Questions));

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ASingleAttemptDisablesRetry()
    {
        var handler = new FakeHttpHandler("rate limited", (HttpStatusCode)429);

        await Assert.ThrowsAsync<JevTransportException>(() => Client(handler, maxAttempts: 1).AskAsync(State, Questions));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EachAttemptResendsTheSameBody()
    {
        var handler = new FakeHttpHandler(
        [
            ((HttpStatusCode)429, "rate limited"),
            (HttpStatusCode.OK, SuccessBody),
        ]);

        await Client(handler, maxAttempts: 2).AskAsync(State, Questions);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.NotEmpty(handler.Requests[1].Body);
        Assert.Equal("Bearer jev-key", handler.Requests[1].Headers["Authorization"]);
    }

    private static IJevClient Client(FakeHttpHandler handler, int maxAttempts)
    {
        var options = new JevProviderOptions
        {
            CompatibleApiKey = "jev-key",
            CompatibleBaseUrl = "https://api.openjev.test/v1/systemone",
            Retry = new JevRetryOptions
            {
                MaxAttempts = maxAttempts,

                // No real wait: the test checks retry policy, not the clock.
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
            },
        };

        var retry = new JevRetryHandler(options.Retry) { InnerHandler = handler };

        return JevClients.Create(JevProviderKind.Compatible, new HttpClient(retry), options);
    }
}
