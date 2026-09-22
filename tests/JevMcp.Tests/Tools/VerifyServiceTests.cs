using System.Text.Json;
using JevMcp.Core;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class VerifyServiceTests
{
    private static readonly TextItem[] SingleEvidence = [new(null, "The sky is blue.")];

    [Fact]
    public async Task VerifiedClaimAboveAutoAcceptStandsOnItsOwn()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["relation_claim0"] = FakeAnswers.Choice(
                "supports",
                0.9,
                ("supports", 0.95),
                ("contradicts", 0.02),
                ("says_nothing", 0.03)),
        });

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["The sky is blue."], SingleEvidence));

        var claim = Assert.Single(result.Results);
        Assert.Equal("claim0", claim.Id);
        Assert.Equal("verified", claim.Verdict);
        Assert.Equal("auto", claim.Action);
        Assert.Null(claim.Status);
        Assert.Equal(0.8, result.AutoAccept);
        Assert.Equal(new VerifySummary(1, 0, 0, 0), result.Summary);
    }

    [Fact]
    public async Task ConfidenceBelowAutoAcceptFlagsReviewWithoutChangingTheVerdict()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["relation_claim0"] = FakeAnswers.Choice(
                "contradicts",
                0.4,
                ("supports", 0.1),
                ("contradicts", 0.8),
                ("says_nothing", 0.1)),
        });

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["The sky is green."], SingleEvidence));

        var claim = Assert.Single(result.Results);
        Assert.Equal("contradicted", claim.Verdict);
        Assert.Equal("review", claim.Action);
        Assert.Equal(new VerifySummary(0, 1, 0, 1), result.Summary);
    }

    [Fact]
    public async Task MissingConfidenceLeavesTheVerdictStandingButUnattested()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["relation_claim0"] = FakeAnswers.Choice(
                "says_nothing",
                null,
                ("supports", 0.1),
                ("contradicts", 0.1),
                ("says_nothing", 0.8)),
        });

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["Unrelated."], SingleEvidence));

        var claim = Assert.Single(result.Results);
        Assert.Equal("unsupported", claim.Verdict);
        Assert.Equal("review", claim.Action);
        Assert.Null(claim.Confidence);
        Assert.Null(claim.Status);
    }

    [Fact]
    public async Task MalformedRelationBecomesAnUnknownVerdictInsteadOfAGuess()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            // Probabilities that do not sum to 1: the Choice contract rejects them.
            ["relation_claim0"] = FakeAnswers.Choice(
                "supports",
                0.9,
                ("supports", 0.5),
                ("contradicts", 0.1),
                ("says_nothing", 0.1)),
        });

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["The sky is blue."], SingleEvidence));

        var claim = Assert.Single(result.Results);
        Assert.Equal("unknown", claim.Verdict);
        Assert.Equal("invalid_response", claim.Status);
        Assert.Equal("review", claim.Action);
        Assert.Null(claim.Probabilities);
    }

    [Fact]
    public async Task OneBrokenClaimDoesNotContaminateTheOthers()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["relation_claim0"] = FakeAnswers.Choice(
                "supports",
                0.9,
                ("supports", 0.9),
                ("contradicts", 0.05),
                ("says_nothing", 0.05)),
            // Choice outside the expected keys: only claim 1 fails.
            ["relation_claim1"] = FakeAnswers.Choice(
                "maybe",
                0.9,
                ("supports", 0.9),
                ("contradicts", 0.05),
                ("says_nothing", 0.05)),
        });

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["first", "second"], SingleEvidence));

        Assert.Equal("verified", result.Results[0].Verdict);
        Assert.Null(result.Results[0].Status);
        Assert.Equal("invalid_response", result.Results[1].Status);
        Assert.Equal(new VerifySummary(1, 0, 0, 1), result.Summary);
    }

    [Fact]
    public async Task AnEmptyEnvelopeInvalidatesEveryClaim()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["first", "second"], SingleEvidence));

        Assert.All(result.Results, claim => Assert.Equal("invalid_response", claim.Status));
        Assert.Equal(new VerifySummary(0, 0, 0, 2), result.Summary);
    }

    [Fact]
    public async Task BrokenConfidenceInvalidatesTheClaimWhileMissingConfidenceDoesNot()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            // Confidence present but outside [0,1]: the response arrived broken, not missing.
            ["relation_claim0"] = FakeAnswers.Choice(
                "supports",
                7,
                ("supports", 0.9),
                ("contradicts", 0.05),
                ("says_nothing", 0.05)),
        });

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["c"], SingleEvidence));

        Assert.Equal("invalid_response", Assert.Single(result.Results).Status);
    }

    [Fact]
    public async Task SourceQuestionOnlyAppearsWithMoreThanOneEvidenceItem()
    {
        var single = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));
        await new VerifyService(single).JudgeAsync(new VerifyRequest(["c"], SingleEvidence));
        Assert.DoesNotContain("source_claim0", single.Questions!.Keys);

        var many = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["relation_claim0"] = FakeAnswers.Choice(
                "supports",
                0.9,
                ("supports", 0.9),
                ("contradicts", 0.05),
                ("says_nothing", 0.05)),
            ["source_claim0"] = FakeAnswers.Choice(
                "doc-b",
                0.9,
                ("doc-a", 0.1),
                ("doc-b", 0.8),
                ("none", 0.1)),
        });

        var result = await new VerifyService(many).JudgeAsync(new VerifyRequest(
            ["c"],
            [new("doc-a", "first"), new("doc-b", "second")]));

        Assert.Contains("source_claim0", many.Questions!.Keys);
        Assert.Equal("doc-b", Assert.Single(result.Results).SupportingEvidence);
    }

    [Fact]
    public async Task SourceNoneIsReportedAsNoSupportingEvidence()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["relation_claim0"] = FakeAnswers.Choice(
                "says_nothing",
                0.9,
                ("supports", 0.05),
                ("contradicts", 0.05),
                ("says_nothing", 0.9)),
            ["source_claim0"] = FakeAnswers.Choice(
                "none",
                0.9,
                ("doc-a", 0.1),
                ("doc-b", 0.1),
                ("none", 0.8)),
        });

        var result = await new VerifyService(client).JudgeAsync(new VerifyRequest(
            ["c"],
            [new("doc-a", "first"), new("doc-b", "second")]));

        Assert.Null(Assert.Single(result.Results).SupportingEvidence);
    }

    [Fact]
    public async Task DuplicateClaimsGetDistinctIdsAndDistinctQuestions()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["same", "same"], SingleEvidence));

        Assert.Equal(new[] { "claim0", "claim1" }, result.Results.Select(claim => claim.Id));
        Assert.Contains("relation_claim0", client.Questions!.Keys);
        Assert.Contains("relation_claim1", client.Questions.Keys);
    }

    [Fact]
    public async Task RejectsEmptyClaimsEmptyEvidenceAndOutOfRangeAutoAccept()
    {
        var service = new VerifyService(new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.JudgeAsync(new VerifyRequest([], SingleEvidence)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.JudgeAsync(new VerifyRequest(["c"], [])));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.JudgeAsync(new VerifyRequest(["c"], SingleEvidence, 1.5)));
    }

    [Fact]
    public async Task OutputUsesSnakeCaseAndOmitsStatusWhenTheAnswerIsValid()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["relation_claim0"] = FakeAnswers.Choice(
                "supports",
                0.9,
                ("supports", 0.9),
                ("contradicts", 0.05),
                ("says_nothing", 0.05)),
        });

        var result = await new VerifyService(client)
            .JudgeAsync(new VerifyRequest(["The sky is blue."], SingleEvidence));

        using var document = JsonDocument.Parse(JevJson.Serialize(result));
        var root = document.RootElement;

        Assert.Equal("jev_verify", root.GetProperty("tool").GetString());
        Assert.Equal("typesafe", root.GetProperty("provider").GetString());
        Assert.Equal(0.8, root.GetProperty("auto_accept").GetDouble());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("verified").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("needs_review").GetInt32());
        Assert.Equal(11, root.GetProperty("usage").GetProperty("input_tokens").GetInt32());

        var claim = root.GetProperty("results")[0];
        Assert.False(claim.TryGetProperty("status", out _));
        Assert.Equal(0.9, claim.GetProperty("probabilities").GetProperty("supports").GetDouble());
    }
}
