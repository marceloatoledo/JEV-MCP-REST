using System.Diagnostics;
using System.Text.Json;
using JevMcp.Core;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class ExtractServiceTests
{
    private const string Document = "Invoice 2024-11-02. Total $42.50, shipping $7.00. Paid on 2024-11-05.";

    private static JevAnswer Pick(string choice, int candidates, double top, double? confidence = 0.9)
    {
        var keys = Enumerable.Range(0, candidates).Select(index => $"c{index}").Append("none_of_them").ToArray();
        var rest = (1 - top) / (keys.Length - 1);

        return FakeAnswers.Choice(
            choice,
            confidence,
            [.. keys.Select(key => (key, key == choice ? top : rest))]);
    }

    [Fact]
    public async Task TheValueIsALiteralSubstringChosenAmongTheMatches()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("c0", 2, 0.95),
        });

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            Document,
            [new("total", @"\$\d+\.\d{2}", "The order total")]));

        var field = Assert.Single(result.Results);
        Assert.Equal("total", field.Id);
        Assert.Equal("$42.50", field.Value);
        Assert.Equal("auto", field.Status);
        Assert.Equal(2, field.CandidatesConsidered);
        Assert.Contains(field.Value!, Document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFieldWithoutMatchesNeverReachesTheModel()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("c0", 2, 0.95),
        });

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            Document,
            [new("total", @"\$\d+\.\d{2}", "The order total"), new("iban", "[A-Z]{2}[0-9]{20}", "The IBAN")]));

        Assert.Equal("not_found", result.Results[1].Status);
        Assert.Equal("no_regex_matches", result.Results[1].Reason);
        Assert.Equal(new[] { "f0" }, client.Questions!.Keys);
    }

    [Fact]
    public async Task WithNoMatchesAnywhereThereIsNoModelCallAtAll()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            Document,
            [new("iban", "[A-Z]{2}[0-9]{20}", "The IBAN")]));

        Assert.Null(client.Questions);
        Assert.Equal("none", result.Provider);
        Assert.Equal(JevMcp.Providers.JevUsage.None, result.Usage);
        Assert.Equal("not_found", Assert.Single(result.Results).Status);
    }

    [Fact]
    public async Task AnInvalidPatternFailsItsOwnFieldOnly()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f1"] = Pick("c0", 2, 0.95),
        });

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            Document,
            [new("broken", "(unclosed", "A broken pattern"), new("total", @"\$\d+\.\d{2}", "The order total")]));

        Assert.Equal("invalid_pattern", result.Results[0].Status);
        Assert.NotNull(result.Results[0].Reason);
        Assert.Equal("auto", result.Results[1].Status);
        Assert.Equal("$42.50", result.Results[1].Value);
    }

    [Fact]
    public async Task AnInvalidFlagIsAPatternErrorNotACrash()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            Document,
            [new("total", @"\$\d+", "The order total", "q")]));

        Assert.Equal("invalid_pattern", Assert.Single(result.Results).Status);
    }

    [Fact]
    public async Task APathologicalPatternReturnsWithinTheDeadline()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));
        var document = new string('a', 40) + "!";
        var elapsed = Stopwatch.StartNew();

        // Lookbehind blocks the non-backtracking engine, so this pattern falls back to
        // the ordinary engine, where the only containment is the execution deadline.
        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            document,
            [new("evil", "(?<=a)(a+)+$", "A catastrophic pattern")]));

        elapsed.Stop();

        Assert.Equal("invalid_pattern", Assert.Single(result.Results).Status);
        Assert.Contains("timed out", result.Results[0].Reason!, StringComparison.Ordinal);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"took {elapsed.Elapsed}");
    }

    [Fact]
    public async Task NoneOfThemBecomesNotFoundOnlyWhenTheModelIsConfident()
    {
        var confident = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("none_of_them", 2, 0.95),
        });

        var decided = await new ExtractService(confident).JudgeAsync(new ExtractRequest(
            Document,
            [new("total", @"\$\d+\.\d{2}", "The order total")]));

        Assert.Equal("not_found", decided.Results[0].Status);
        Assert.Equal("none_matched", decided.Results[0].Reason);

        var unsure = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("none_of_them", 2, 0.5),
        });

        var hedged = await new ExtractService(unsure).JudgeAsync(new ExtractRequest(
            Document,
            [new("total", @"\$\d+\.\d{2}", "The order total")]));

        Assert.Equal("review", hedged.Results[0].Status);
        Assert.Equal("none_matched_ambiguous", hedged.Results[0].Reason);
    }

    [Fact]
    public async Task ATruncatedCandidateSetCanNeverBeAutoAccepted()
    {
        var document = string.Join(" ", Enumerable.Range(1, Limits.MaxExtractCandidates + 5)
            .Select(index => $"v{index}"));

        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("c0", Limits.MaxExtractCandidates, 0.99),
        });

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            document,
            [new("version", @"v\d+", "A version token")]));

        var field = Assert.Single(result.Results);
        Assert.True(field.CandidatesTruncated);
        Assert.Equal("review", field.Status);
        Assert.Equal("candidate_limit", field.Reason);
        Assert.Equal("v1", field.Value);
    }

    [Fact]
    public async Task ATruncatedSetAlsoBlocksADefinitiveNotFound()
    {
        var document = string.Join(" ", Enumerable.Range(1, Limits.MaxExtractCandidates + 5)
            .Select(index => $"v{index}"));

        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("none_of_them", Limits.MaxExtractCandidates, 0.99),
        });

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            document,
            [new("version", @"v\d+", "A version token")]));

        Assert.Equal("review", result.Results[0].Status);
        Assert.Equal("candidate_limit", result.Results[0].Reason);
    }

    [Fact]
    public async Task OverlongMatchesAreSkippedAndReported()
    {
        var document = new string('x', Limits.MaxExtractCandidateChars + 10);

        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            document,
            [new("blob", "x+", "A long blob")]));

        var field = Assert.Single(result.Results);
        Assert.Equal(1, field.MatchesSkippedTooLong);
        Assert.Equal("review", field.Status);
        Assert.Equal("matches_too_long", field.Reason);
        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task DuplicateMatchesAreOfferedOnce()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("c0", 1, 0.99),
        });

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            "v2 and v2 and v2",
            [new("version", @"v\d+", "A version token")]));

        Assert.Equal(1, result.Results[0].CandidatesConsidered);
        Assert.Equal("v2", result.Results[0].Value);
    }

    [Fact]
    public async Task MalformedAnswerForAFieldWithCandidatesIsReported()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("c9", 2, 0.95),
        });

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            Document,
            [new("total", @"\$\d+\.\d{2}", "The order total")]));

        Assert.Equal("invalid_response", result.Results[0].Status);
        Assert.Null(result.Results[0].Value);
    }

    [Fact]
    public async Task InvalidFieldIdsAndDocumentLimitsAreRejected()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));
        var service = new ExtractService(client);

        await Assert.ThrowsAsync<ArgumentException>(() => service.JudgeAsync(new ExtractRequest(
            Document,
            [new("Total", @"\$\d+", "uppercase is not a slug")])));

        await Assert.ThrowsAsync<ArgumentException>(() => service.JudgeAsync(new ExtractRequest(
            Document,
            [new("total", @"\$\d+", "first"), new("total", @"\$\d+", "second")])));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.JudgeAsync(new ExtractRequest(
            new string('x', Limits.MaxExtractDocumentChars + 1),
            [new("total", @"\$\d+", "The order total")])));

        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task OutputUsesSnakeCaseFieldNames()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["f0"] = Pick("c0", 2, 0.95),
        });

        var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
            Document,
            [new("total", @"\$\d+\.\d{2}", "The order total")]));

        using var document = JsonDocument.Parse(JevJson.Serialize(result));
        var root = document.RootElement;

        Assert.Equal("jev_extract", root.GetProperty("tool").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("extracted").GetInt32());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("auto").GetInt32());

        var field = root.GetProperty("results")[0];
        Assert.Equal(2, field.GetProperty("candidates_considered").GetInt32());
        Assert.False(field.GetProperty("candidates_truncated").GetBoolean());
        Assert.Equal(0, field.GetProperty("matches_skipped_too_long").GetInt32());
    }
}
