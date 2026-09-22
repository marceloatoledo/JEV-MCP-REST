using System.Text.Json;
using JevMcp.Core;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class FindServiceTests
{
    private static readonly TextItem[] Three =
    [
        new("a", "Retries live in the HTTP handler."),
        new("b", "The parser rejects malformed envelopes."),
        new("c", "Tokens are stored in the admin database."),
    ];

    private static FakeJevClient Client(double exists, params (string Key, double Probability)[] probabilities)
    {
        var top = probabilities.MaxBy(entry => entry.Probability).Key;

        return new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["exists"] = FakeAnswers.Noul(exists),
            ["best"] = FakeAnswers.Choice(top, 0.8, probabilities),
        });
    }

    [Fact]
    public async Task RanksCandidatesByProbabilityAndReportsTheExistsVerdict()
    {
        var client = Client(0.95, ("a", 0.7), ("b", 0.2), ("c", 0.1));

        var result = await new FindService(client).JudgeAsync(new FindRequest("where do retries live?", Three));

        Assert.Equal(new[] { "a", "b", "c" }, result.Top.Select(hit => hit.Id));
        Assert.Equal(0.7, result.Top[0].Probability);
        Assert.Equal("Retries live in the HTTP handler.", result.Top[0].Text);
        Assert.Equal(0.95, result.Exists);
        Assert.Equal("answered", result.ExistsVerdict);
        Assert.Null(result.Status);
    }

    [Fact]
    public async Task LowExistsIsReportedEvenWhenTheRankingLooksConfident()
    {
        var client = Client(0.05, ("a", 0.9), ("b", 0.05), ("c", 0.05));

        var result = await new FindService(client).JudgeAsync(new FindRequest("unrelated question", Three));

        Assert.Equal("absent", result.ExistsVerdict);
        Assert.Equal("a", result.Top[0].Id);
    }

    [Fact]
    public async Task TopKTrimsTheRankingAndDefaultsToFive()
    {
        var client = Client(0.9, ("a", 0.6), ("b", 0.3), ("c", 0.1));

        var trimmed = await new FindService(client).JudgeAsync(new FindRequest("q", Three, 2));
        Assert.Equal(2, trimmed.Top.Count);

        var full = await new FindService(client).JudgeAsync(new FindRequest("q", Three));
        Assert.Equal(3, full.Top.Count);
    }

    [Fact]
    public async Task CandidatesWithoutIdsGetIndexedIdsAndLongTextIsTruncated()
    {
        var text = new string('x', Limits.MaxCandidateChars + 50);
        var client = Client(0.9, ("candidate0", 0.6), ("candidate1", 0.4));

        var result = await new FindService(client).JudgeAsync(new FindRequest(
            "q",
            [new(null, text), new(null, "short")]));

        Assert.Equal(new[] { "candidate0", "candidate1" }, result.Top.Select(hit => hit.Id));
        Assert.Equal(Excerpts.Truncate(text, Limits.MaxCandidateChars), result.Top[0].Text);
        Assert.True(result.Top[0].Text.Length < text.Length);
    }

    [Fact]
    public async Task MissingBestOrExistsIsReportedInsteadOfAnEmptyRanking()
    {
        var onlyExists = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["exists"] = FakeAnswers.Noul(0.9),
        });

        var result = await new FindService(onlyExists).JudgeAsync(new FindRequest("q", Three));

        Assert.Equal("invalid_response", result.Status);
        Assert.Empty(result.Top);
        Assert.Null(result.ExistsVerdict);
        Assert.Equal(0.9, result.Exists);
    }

    [Fact]
    public async Task RejectsEmptyQueryNoCandidatesTooManyCandidatesAndBadTopK()
    {
        var service = new FindService(Client(0.9, ("a", 1.0)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.JudgeAsync(new FindRequest("", Three)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.JudgeAsync(new FindRequest("q", [])));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.JudgeAsync(new FindRequest("q", Three, 0)));

        var tooMany = Enumerable
            .Range(0, Limits.MaxCandidates + 1)
            .Select(index => new TextItem(null, $"candidate {index}"))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.JudgeAsync(new FindRequest("q", tooMany)));
    }

    [Fact]
    public async Task OutputUsesSnakeCaseFieldNames()
    {
        var client = Client(0.9, ("a", 0.6), ("b", 0.3), ("c", 0.1));

        var result = await new FindService(client).JudgeAsync(new FindRequest("q", Three, 1));

        using var document = JsonDocument.Parse(JevJson.Serialize(result));
        var root = document.RootElement;

        Assert.Equal("jev_find", root.GetProperty("tool").GetString());
        Assert.Equal("q", root.GetProperty("query").GetString());
        Assert.Equal("answered", root.GetProperty("exists_verdict").GetString());
        Assert.Equal("a", root.GetProperty("top")[0].GetProperty("id").GetString());
        Assert.False(root.TryGetProperty("status", out _));
    }
}
