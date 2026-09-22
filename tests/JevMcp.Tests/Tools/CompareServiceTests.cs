using System.Text.Json;
using JevMcp.Core;
using JevMcp.Providers;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class CompareServiceTests
{
    private static JevAnswer Relation(string choice, double top, double? confidence = 0.9)
    {
        var rest = (1 - top) / 2;
        var keys = new[] { "same_fact", "contradicts", "different_facts" };

        return FakeAnswers.Choice(
            choice,
            confidence,
            [.. keys.Select(key => (key, key == choice ? top : rest))]);
    }

    [Fact]
    public async Task OverallRelationComesWithDistributionMarginAndDecision()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["overall"] = Relation("contradicts", 0.94),
        });

        var result = await new CompareService(client).JudgeAsync(new CompareRequest(
            "The launch is on March 3rd.",
            "The launch is on May 12th."));

        Assert.Equal("contradicts", result.Overall.Relation);
        Assert.Equal("auto", result.Overall.Decision);
        Assert.Equal(0.94, result.Overall.Probabilities!["contradicts"]);
        Assert.Equal(0.91, result.Overall.Margin!.Value, 10);
        Assert.Empty(result.Aspects);
        Assert.Null(result.Overall.Status);
    }

    [Fact]
    public async Task EachAspectIsJudgedOnItsOwnAndMayDisagreeWithTheOverall()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["overall"] = Relation("same_fact", 0.9),
            ["aspect_0"] = Relation("contradicts", 0.92),
            ["aspect_1"] = Relation("same_fact", 0.93),
        });

        var result = await new CompareService(client).JudgeAsync(new CompareRequest(
            "Ships in March for $30.",
            "Ships in March for $40.",
            ["price", "launch date"]));

        Assert.Equal("same_fact", result.Overall.Relation);
        Assert.Equal(new[] { "price", "launch date" }, result.Aspects.Select(aspect => aspect.Aspect));
        Assert.Equal("contradicts", result.Aspects[0].Relation);
        Assert.Equal("same_fact", result.Aspects[1].Relation);
    }

    [Fact]
    public async Task AMalformedAspectDoesNotContaminateTheOverall()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["overall"] = Relation("same_fact", 0.95),
        });

        var result = await new CompareService(client).JudgeAsync(new CompareRequest("a", "b", ["price"]));

        Assert.Equal("same_fact", result.Overall.Relation);
        Assert.Null(result.Overall.Status);
        Assert.Equal("invalid_response", result.Aspects[0].Status);
        Assert.Null(result.Aspects[0].Relation);
        Assert.Equal("review", result.Aspects[0].Decision);
    }

    [Fact]
    public async Task PassageAboveTheCapIsRejectedBeforeTheModel()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));
        var service = new CompareService(client);
        var huge = new string('x', Limits.MaxComparePassageChars + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.JudgeAsync(new CompareRequest(huge, "b")));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.JudgeAsync(new CompareRequest("a", huge)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.JudgeAsync(new CompareRequest("", "b")));

        var tooManyAspects = Enumerable.Range(0, Limits.MaxCompareAspects + 1)
            .Select(index => $"aspect {index}")
            .ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.JudgeAsync(new CompareRequest("a", "b", tooManyAspects)));

        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task AspectQuestionsUseTheAspectSpecificWording()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["overall"] = Relation("same_fact", 0.95),
            ["aspect_0"] = Relation("same_fact", 0.95),
        });

        await new CompareService(client).JudgeAsync(new CompareRequest("a", "b", ["price"]));

        var aspect = (ChoiceQuestion)client.Questions!["aspect_0"];
        var different = aspect.Criteria.Single(criterion => criterion.Key == "different_facts");

        Assert.Contains("does not address it", different.Description!, StringComparison.Ordinal);
        Assert.Contains("price", aspect.Instructions.ToNode().GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutputUsesSnakeCaseFieldNames()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["overall"] = Relation("same_fact", 0.95),
            ["aspect_0"] = Relation("same_fact", 0.95),
        });

        var result = await new CompareService(client).JudgeAsync(new CompareRequest("a", "b", ["price"]));

        using var document = JsonDocument.Parse(JevJson.Serialize(result));
        var root = document.RootElement;

        Assert.Equal("jev_compare", root.GetProperty("tool").GetString());
        Assert.Equal("same_fact", root.GetProperty("overall").GetProperty("relation").GetString());
        Assert.Equal("price", root.GetProperty("aspects")[0].GetProperty("aspect").GetString());
        Assert.Equal(0.5, root.GetProperty("thresholds").GetProperty("minimum_margin").GetDouble());
    }
}
