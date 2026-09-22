using System.Text.Json;
using JevMcp.Core;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class RerankServiceTests
{
    private static readonly TextItem[] Three =
    [
        new("a", "Retries live in the HTTP handler."),
        new("b", "The parser rejects malformed envelopes."),
        new("c", "Tokens are stored in the admin database."),
    ];

    private static FakeJevClient Client(params double?[] scores)
    {
        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);

        for (var index = 0; index < scores.Length; index++)
        {
            if (scores[index] is { } score)
            {
                answers[$"rel_{index}"] = FakeAnswers.Noul(score);
            }
        }

        return new FakeJevClient(answers);
    }

    [Fact]
    public async Task EveryCandidateIsScoredAndTheWholeListSurvivesSorted()
    {
        var result = await new RerankService(Client(0.2, 0.9, 0.5))
            .JudgeAsync(new RerankRequest("where do retries live?", Three));

        var ranked = Assert.IsAssignableFrom<IReadOnlyList<RerankHit>>(result.Ranked);
        Assert.Equal(new[] { "b", "c", "a" }, ranked.Select(hit => hit.Id));
        Assert.Equal(new[] { 1, 2, 3 }, ranked.Select(hit => hit.Rank));
        Assert.Equal(0.9, ranked[0].Relevance);
        Assert.Equal(new RerankSummary(3, 3), result.Summary);
        Assert.Null(result.Status);
        Assert.Equal("auto", result.Action);
    }

    [Fact]
    public async Task TopKTrimsTheAnswerButNotTheScoring()
    {
        var client = Client(0.2, 0.9, 0.5);

        var result = await new RerankService(client).JudgeAsync(new RerankRequest("q", Three, 2));

        Assert.Equal(new RerankSummary(3, 2), result.Summary);
        Assert.Equal(new[] { "b", "c" }, result.Ranked!.Select(hit => hit.Id));
        Assert.Equal(3, client.Questions!.Count);
    }

    [Fact]
    public async Task OneMissingScoreInvalidatesTheWholeOrdering()
    {
        var result = await new RerankService(Client(0.9, null, 0.5))
            .JudgeAsync(new RerankRequest("q", Three));

        // Treating the missing score as zero would push it to the end as if it were irrelevant.
        Assert.Equal("invalid_response", result.Status);
        Assert.Null(result.Ranked);
        Assert.Null(result.Summary);
        Assert.Equal("escalate", result.Action);
    }

    [Fact]
    public async Task TiedScoresKeepTheCallerOrder()
    {
        var result = await new RerankService(Client(0.5, 0.5, 0.5))
            .JudgeAsync(new RerankRequest("q", Three));

        Assert.Equal(new[] { "a", "b", "c" }, result.Ranked!.Select(hit => hit.Id));
    }

    [Fact]
    public async Task CandidatesWithoutIdsAreNumberedAndLongTextIsTruncated()
    {
        var text = new string('x', Limits.MaxCandidateChars + 50);

        var result = await new RerankService(Client(0.9, 0.1))
            .JudgeAsync(new RerankRequest("q", [new(null, text), new(null, "short")]));

        Assert.Equal("candidate0", result.Ranked![0].Id);
        Assert.Equal(Excerpts.Truncate(text, Limits.MaxCandidateChars), result.Ranked[0].Text);
    }

    [Fact]
    public async Task TheQueryTravelsOnceInTheStateAndEachQuestionCarriesItsCandidate()
    {
        var client = Client(0.9, 0.1, 0.1);

        await new RerankService(client).JudgeAsync(new RerankRequest("where do retries live?", Three));

        Assert.Equal("where do retries live?", client.State!["query"]!.GetValue<string>());
        Assert.Equal(new[] { "rel_0", "rel_1", "rel_2" }, client.Questions!.Keys);
        Assert.Contains(
            "Retries live in the HTTP handler.",
            client.Questions["rel_0"].Instructions.ToNode().GetValue<string>(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BudgetsAndDuplicateIdsAreRejectedBeforeTheModel()
    {
        var client = Client();
        var service = new RerankService(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.JudgeAsync(new RerankRequest("", Three)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.JudgeAsync(new RerankRequest("q", [new("dup", "a"), new("dup", "b")])));

        var tooMany = Enumerable.Range(0, Limits.MaxRerankCandidates + 1)
            .Select(index => new TextItem(null, "x"))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.JudgeAsync(new RerankRequest("q", tooMany)));

        // 250 candidates of 2,000 characters exceed the 100,000 aggregated budget.
        var heavy = Enumerable.Range(0, Limits.MaxRerankCandidates)
            .Select(index => new TextItem(null, new string('x', Limits.MaxCandidateChars)))
            .ToArray();

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.JudgeAsync(new RerankRequest("q", heavy)));

        Assert.Contains("character budget", error.Message, StringComparison.Ordinal);
        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task OutputUsesSnakeCaseFieldNames()
    {
        var result = await new RerankService(Client(0.9, 0.2, 0.1))
            .JudgeAsync(new RerankRequest("q", Three, 1));

        using var document = JsonDocument.Parse(JevJson.Serialize(result));
        var root = document.RootElement;

        Assert.Equal("jev_rerank", root.GetProperty("tool").GetString());
        Assert.Equal(3, root.GetProperty("summary").GetProperty("candidates").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("returned").GetInt32());
        Assert.Equal("a", root.GetProperty("ranked")[0].GetProperty("id").GetString());
        Assert.False(root.TryGetProperty("status", out _));
        Assert.Equal("auto", root.GetProperty("action").GetString());
    }
}
