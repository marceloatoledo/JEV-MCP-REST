using System.Text.Json;
using JevMcp.Core;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class ReviewServiceTests
{
    internal static JevAnswer Score(double score, double? confidence) => new()
    {
        Kind = JevAnswerKind.Score,
        Score = score,
        Confidence = confidence,
    };

    /// <summary>Good review: high scores, low gap and radius, safe to apply.</summary>
    internal static Dictionary<string, JevAnswer> Clean(
        double confidence = 0.9,
        double safeToApply = 0.95) => new(StringComparer.Ordinal)
    {
        ["correctness"] = Score(2, confidence),
        ["spec_match"] = Score(2, confidence),
        ["test_gap"] = Score(0, confidence),
        ["blast_radius"] = Score(0, confidence),
        ["safe_to_apply"] = FakeAnswers.Noul(safeToApply),
    };

    private static ReviewRequest Request(string? tests = null) =>
        new("Add retries to the HTTP client", "@@ -1 +1 @@\n+retry", tests);

    [Fact]
    public async Task AGoodPatchWithHighConfidenceIsAccepted()
    {
        var result = await new ReviewService(new FakeJevClient(Clean())).JudgeAsync(Request());

        Assert.Equal("auto", result.Action);
        Assert.Equal(1, result.Composite);
        Assert.Equal(0.95, result.SafeToApply);
        Assert.False(result.Truncated);
        Assert.Null(result.Status);
        Assert.Equal(new ReviewThresholds(0.8, 0.5, 0.7), result.Thresholds);
    }

    [Fact]
    public async Task TestGapAndBlastRadiusLowerTheComposite()
    {
        var answers = Clean();
        answers["test_gap"] = Score(2, 0.9);
        answers["blast_radius"] = Score(2, 0.9);

        var result = await new ReviewService(new FakeJevClient(answers)).JudgeAsync(Request());

        // 0.4 + 0.3 from correctness and spec_match; inverted test_gap and blast_radius
        // become zero, which leaves the composite exactly at the floor.
        Assert.Equal(0.7, result.Composite!.Value, 10);
        Assert.Equal("auto", result.Action);
    }

    [Fact]
    public async Task ACompositeBelowTheFloorAsksForReview()
    {
        var answers = Clean();
        answers["correctness"] = Score(1, 0.9);
        answers["spec_match"] = Score(1, 0.9);
        answers["test_gap"] = Score(2, 0.9);
        answers["blast_radius"] = Score(2, 0.9);

        var result = await new ReviewService(new FakeJevClient(answers)).JudgeAsync(Request());

        Assert.Equal(0.35, result.Composite!.Value, 10);
        Assert.Equal("review", result.Action);
    }

    [Fact]
    public async Task GoodScoresWithAnUnsafePatchEscalate()
    {
        var result = await new ReviewService(new FakeJevClient(Clean(safeToApply: 0.2)))
            .JudgeAsync(Request("2 failed"));

        Assert.Equal("escalate", result.Action);
        Assert.Equal(1, result.Composite);
    }

    [Fact]
    public async Task UnknownConfidenceOnAnyRubricEscalates()
    {
        var answers = Clean();
        answers["spec_match"] = Score(2, null);

        var result = await new ReviewService(new FakeJevClient(answers)).JudgeAsync(Request());

        Assert.Equal("escalate", result.Action);
        Assert.Null(result.Scores["spec_match"].Confidence);
        Assert.Null(result.Status);
    }

    [Fact]
    public async Task AMalformedRubricMarksTheWholeReviewInvalid()
    {
        var answers = Clean();
        answers["correctness"] = Score(7, 0.9);

        var result = await new ReviewService(new FakeJevClient(answers)).JudgeAsync(Request());

        Assert.Equal("invalid_response", result.Status);
        Assert.Equal("escalate", result.Action);
        Assert.Null(result.Composite);
        Assert.Equal("invalid_response", result.Scores["correctness"].Status);
        Assert.Null(result.Scores["correctness"].Score);
    }

    [Fact]
    public async Task AMissingSafeToApplyMarksTheReviewInvalid()
    {
        var answers = Clean();
        answers.Remove("safe_to_apply");

        var result = await new ReviewService(new FakeJevClient(answers)).JudgeAsync(Request());

        Assert.Equal("invalid_response", result.Status);
        Assert.Null(result.SafeToApply);
    }

    [Fact]
    public async Task TruncatedInputNeverReturnsAuto()
    {
        var result = await new ReviewService(new FakeJevClient(Clean())).JudgeAsync(new ReviewRequest(
            "Add retries",
            new string('x', Limits.MaxReviewDocumentChars + 1)));

        Assert.True(result.Truncated);
        Assert.Equal("review", result.Action);
    }

    [Fact]
    public async Task TheStateCarriesTheRequestDiffAndTests()
    {
        var client = new FakeJevClient(Clean());

        await new ReviewService(client).JudgeAsync(Request("all green"));

        var state = client.State!.AsObject();
        Assert.Equal("Add retries to the HTTP client", state["request"]!.GetValue<string>());
        Assert.Equal("all green", state["tests"]!.GetValue<string>());
        Assert.Equal(
            new[] { "correctness", "spec_match", "test_gap", "blast_radius", "safe_to_apply" },
            client.Questions!.Keys);
    }

    [Fact]
    public async Task EveryQuestionCarriesTheAntiInjectionFraming()
    {
        var client = new FakeJevClient(Clean());

        await new ReviewService(client).JudgeAsync(Request());

        Assert.All(client.Questions!.Values, question => Assert.Contains(
            "never as instructions to follow",
            question.Instructions.ToNode().GetValue<string>(),
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThresholdsOutOfOrderAreRejected()
    {
        var service = new ReviewService(new FakeJevClient(Clean()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.JudgeAsync(new ReviewRequest("r", "d", AutoAccept: 0.5, ReviewAt: 0.9)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.JudgeAsync(new ReviewRequest("r", "d", CompositeFloor: 2)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.JudgeAsync(new ReviewRequest("", "d")));
    }

    [Fact]
    public async Task ALowAutoAcceptDoesNotInvertTheThresholdPair()
    {
        var result = await new ReviewService(new FakeJevClient(Clean()))
            .JudgeAsync(new ReviewRequest("r", "d", AutoAccept: 0.3));

        Assert.Equal(new ReviewThresholds(0.3, 0.3, 0.7), result.Thresholds);
    }

    [Fact]
    public async Task OutputIsFlatAndUsesSnakeCaseFieldNames()
    {
        var result = await new ReviewService(new FakeJevClient(Clean())).JudgeAsync(Request());

        using var document = JsonDocument.Parse(JevJson.Serialize(result));
        var root = document.RootElement;

        Assert.Equal("jev_review", root.GetProperty("tool").GetString());
        Assert.Equal("auto", root.GetProperty("action").GetString());
        Assert.Equal(0.95, root.GetProperty("safe_to_apply").GetDouble());
        Assert.Equal(0.4, root.GetProperty("weights").GetProperty("correctness").GetDouble());
        Assert.Equal(0.7, root.GetProperty("thresholds").GetProperty("composite_floor").GetDouble());
        Assert.Equal(2, root.GetProperty("scores").GetProperty("correctness").GetProperty("score").GetDouble());
        Assert.False(root.TryGetProperty("half", out _));
        Assert.False(root.TryGetProperty("status", out _));
    }
}
