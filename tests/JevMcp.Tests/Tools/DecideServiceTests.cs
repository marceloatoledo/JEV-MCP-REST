using System.Text.Json;
using JevMcp.Core;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class DecideServiceTests
{
    private static readonly DecideCandidate[] TwoOptions =
    [
        new("keep_polling", "Keep the polling loop and raise the interval"),
        new("use_webhook", "Switch to the provider webhook"),
    ];

    private static DecideRequest Request(
        IReadOnlyList<DecideCandidate>? candidates = null,
        IReadOnlyList<string>? requirements = null,
        bool? escapeHatches = null) =>
        new(
            "How should the integration receive updates?",
            "Polling costs 40k requests per day; the provider publishes webhooks with 5s latency.",
            "Lower cost, keep latency under a minute.",
            candidates ?? TwoOptions,
            requirements,
            escapeHatches);

    private static JevAnswer Recommendation(string choice, double? confidence, params (string, double)[] probabilities)
        => FakeAnswers.Choice(choice, confidence, probabilities);

    [Fact]
    public async Task RecommendationComesBackWithTheCallerIds()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["recommendation"] = Recommendation(
                "option_1",
                0.82,
                ("option_0", 0.1),
                ("option_1", 0.8),
                ("ask_user", 0.04),
                ("investigate", 0.03),
                ("none", 0.03)),
        });

        var result = await new DecideService(client).JudgeAsync(Request());

        Assert.Equal("use_webhook", result.Recommendation.Selected);
        Assert.False(result.Recommendation.Escaped);
        Assert.Equal(0.82, result.Recommendation.Confidence);
        Assert.Equal("auto", result.Action);
        Assert.Equal(0.8, result.Recommendation.Probabilities!["use_webhook"]);
        Assert.Contains("ask_user", result.Recommendation.Probabilities.Keys);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task AnEscapeHatchIsReportedAsSuch()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["recommendation"] = Recommendation(
                "ask_user",
                0.7,
                ("option_0", 0.1),
                ("option_1", 0.1),
                ("ask_user", 0.7),
                ("investigate", 0.05),
                ("none", 0.05)),
        });

        var result = await new DecideService(client).JudgeAsync(Request());

        Assert.Equal("ask_user", result.Recommendation.Selected);
        Assert.True(result.Recommendation.Escaped);
        Assert.Equal("review", result.Action);
    }

    [Fact]
    public async Task EscapeHatchesCanBeTurnedOff()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["recommendation"] = Recommendation("option_0", 0.9, ("option_0", 0.9), ("option_1", 0.1)),
        });

        var result = await new DecideService(client).JudgeAsync(Request(escapeHatches: false));

        Assert.Equal("keep_polling", result.Recommendation.Selected);
        Assert.DoesNotContain("ask_user", result.Recommendation.Probabilities!.Keys);
    }

    [Fact]
    public async Task ARequirementContradictingTheRecommendationIsRaisedAsAWarning()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["recommendation"] = Recommendation(
                "option_1",
                0.9,
                ("option_0", 0.05),
                ("option_1", 0.9),
                ("ask_user", 0.02),
                ("investigate", 0.02),
                ("none", 0.01)),
            ["check_0_0"] = FakeAnswers.Choice(
                "supported",
                0.8,
                ("supported", 0.8),
                ("contradicted", 0.1),
                ("unknown", 0.1)),
            ["check_1_0"] = FakeAnswers.Choice(
                "contradicted",
                0.8,
                ("supported", 0.1),
                ("contradicted", 0.8),
                ("unknown", 0.1)),
        });

        var result = await new DecideService(client).JudgeAsync(Request(requirements: ["must work offline"]));

        Assert.Equal("use_webhook", result.Recommendation.Selected);
        Assert.Equal(1, result.RequirementsChecked);
        Assert.Equal(2, result.Checks.Count);
        Assert.Equal("contradicted", result.Checks[1].Answer);

        // The disagreement appears in full, without averaging with the recommendation.
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("Requirement 1 contradicted", warning, StringComparison.Ordinal);
        Assert.Equal("review", result.Action);
    }

    [Fact]
    public async Task ARequirementContradictingAnotherCandidateIsNotAWarning()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["recommendation"] = Recommendation(
                "option_0",
                0.9,
                ("option_0", 0.9),
                ("option_1", 0.05),
                ("ask_user", 0.02),
                ("investigate", 0.02),
                ("none", 0.01)),
            ["check_1_0"] = FakeAnswers.Choice(
                "contradicted",
                0.8,
                ("supported", 0.1),
                ("contradicted", 0.8),
                ("unknown", 0.1)),
        });

        var result = await new DecideService(client).JudgeAsync(Request(requirements: ["must work offline"]));

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task MalformedCheckIsReportedWithoutInvalidatingTheDecision()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["recommendation"] = Recommendation(
                "option_0",
                0.9,
                ("option_0", 0.9),
                ("option_1", 0.05),
                ("ask_user", 0.02),
                ("investigate", 0.02),
                ("none", 0.01)),
        });

        var result = await new DecideService(client).JudgeAsync(Request(requirements: ["must work offline"]));

        Assert.Equal("keep_polling", result.Recommendation.Selected);
        Assert.All(result.Checks, check => Assert.Equal("invalid_response", check.Answer));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task AMalformedRecommendationIsNotADecision()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));

        var result = await new DecideService(client).JudgeAsync(Request());

        Assert.Equal("invalid_response", result.Recommendation.Status);
        Assert.Null(result.Recommendation.Selected);
        Assert.Null(result.Recommendation.Probabilities);
    }

    [Fact]
    public async Task CandidateCollidingWithAnEscapeHatchIsRejected()
    {
        var service = new DecideService(new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)));

        var colliding = new DecideCandidate[] { new("ask_user", "ask the user"), new("keep", "keep it") };

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.JudgeAsync(Request(colliding)));
        Assert.Contains("collides with an escape hatch", error.Message, StringComparison.Ordinal);

        // With no active escapes there is no possible collision, and the same id passes.
        await service.JudgeAsync(Request(colliding, escapeHatches: false));
    }

    [Fact]
    public async Task InvalidIdsDuplicatesAndCountsAreRejectedBeforeTheModel()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));
        var service = new DecideService(client);

        await Assert.ThrowsAsync<ArgumentException>(() => service.JudgeAsync(
            Request([new("Keep_Polling", "uppercase is not a slug"), new("use_webhook", "ok")])));

        await Assert.ThrowsAsync<ArgumentException>(() => service.JudgeAsync(
            Request([new("same", "first"), new("same", "second")])));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.JudgeAsync(
            Request([new("only", "a single candidate is not a decision")])));

        var tooMany = Enumerable.Range(0, Limits.MaxDecideCandidates + 1)
            .Select(index => new DecideCandidate($"option{index}", $"candidate {index}"))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.JudgeAsync(Request(tooMany)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.JudgeAsync(
            Request(requirements: ["a", "b", "c", "d"])));

        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task OutputUsesSnakeCaseFieldNames()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["recommendation"] = Recommendation(
                "option_0",
                0.9,
                ("option_0", 0.9),
                ("option_1", 0.05),
                ("ask_user", 0.02),
                ("investigate", 0.02),
                ("none", 0.01)),
        });

        var result = await new DecideService(client).JudgeAsync(Request(requirements: ["must work offline"]));

        using var document = JsonDocument.Parse(JevJson.Serialize(result));
        var root = document.RootElement;

        Assert.Equal("jev_decide", root.GetProperty("tool").GetString());
        Assert.Equal(1, root.GetProperty("requirements_checked").GetInt32());
        Assert.Equal("keep_polling", root.GetProperty("recommendation").GetProperty("selected").GetString());
        Assert.False(root.GetProperty("recommendation").GetProperty("escaped").GetBoolean());
        Assert.Equal("auto", root.GetProperty("action").GetString());
        Assert.Equal(0, root.GetProperty("checks")[0].GetProperty("requirement").GetInt32());
    }
}
