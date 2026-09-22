using System.Text.Json;
using JevMcp.Core;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class ClassifyServiceTests
{
    private static readonly ClassDefinition[] TwoClasses =
    [
        new("bug", "A defect report"),
        new("feature", "A request for new behaviour"),
    ];

    private static FakeJevClient Client(params (string Key, string Choice, double Top)[] answers)
    {
        var map = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);

        foreach (var (key, choice, top) in answers)
        {
            var other = choice == "c0" ? "c1" : "c0";
            map[key] = FakeAnswers.Choice(choice, 0.9, (choice, top), (other, 1 - top));
        }

        return new FakeJevClient(map);
    }

    [Fact]
    public async Task HighProbabilityAndClearMarginIsAccepted()
    {
        var client = Client(("i0", "c0", 0.95));

        var result = await new ClassifyService(client).JudgeAsync(new ClassifyRequest(
            [new(null, "the app crashes on start")],
            TwoClasses));

        var item = Assert.Single(result.Results);
        Assert.Equal("bug", item.Classification);
        Assert.Equal("auto", item.Decision);
        Assert.Equal(0.95, item.TopProbability);
        Assert.Equal(0.9, item.Margin!.Value, 10);
        Assert.Equal(new ClassifyThresholds(0.85, 0.5), result.Thresholds);
    }

    [Fact]
    public async Task HighProbabilityWithoutMarginFallsToReview()
    {
        // Top above auto_accept and margin smaller than 0.5: one condition alone is not enough.
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["i0"] = FakeAnswers.Choice("c0", 0.9, ("c0", 0.86), ("c1", 0.14)),
        });

        var result = await new ClassifyService(client).JudgeAsync(new ClassifyRequest(
            [new(null, "text")],
            TwoClasses,
            MinimumMargin: 0.8));

        Assert.Equal("review", Assert.Single(result.Results).Decision);
    }

    [Fact]
    public async Task CallerIdsSurviveAndWireKeysNeverLeak()
    {
        var client = Client(("i0", "c1", 0.95));

        var result = await new ClassifyService(client).JudgeAsync(new ClassifyRequest(
            [new("ticket-9", "please add dark mode")],
            TwoClasses));

        var item = Assert.Single(result.Results);
        Assert.Equal("ticket-9", item.Id);
        Assert.Equal("feature", item.Classification);
        Assert.Equal(new[] { "bug", "feature" }, item.Probabilities!.Keys.Order(StringComparer.Ordinal));

        // What goes to the model is positional: the caller's id does not become an option key.
        Assert.Equal(new[] { "i0" }, client.Questions!.Keys);
    }

    [Fact]
    public async Task ItemsWithoutIdsAreNumberedForTheCaller()
    {
        var client = Client(("i0", "c0", 0.95), ("i1", "c1", 0.95));

        var result = await new ClassifyService(client).JudgeAsync(new ClassifyRequest(
            [new(null, "first"), new(null, "second")],
            TwoClasses));

        Assert.Equal(new[] { "item0", "item1" }, result.Results.Select(item => item.Id));
    }

    [Fact]
    public async Task DuplicateIdIsRejectedInsteadOfSilentlyRenamed()
    {
        var service = new ClassifyService(Client());

        await Assert.ThrowsAsync<ArgumentException>(() => service.JudgeAsync(new ClassifyRequest(
            [new("same", "first"), new("same", "second")],
            TwoClasses)));
    }

    [Fact]
    public async Task MalformedAnswerIsolatesTheItemAndCountsInTheSummary()
    {
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["i0"] = FakeAnswers.Choice("c0", 0.9, ("c0", 0.95), ("c1", 0.05)),
        });

        var result = await new ClassifyService(client).JudgeAsync(new ClassifyRequest(
            [new(null, "first"), new(null, "second")],
            TwoClasses));

        Assert.Null(result.Results[0].Status);
        Assert.Equal("invalid_response", result.Results[1].Status);
        Assert.Null(result.Results[1].Classification);
        Assert.Equal("review", result.Results[1].Decision);
        Assert.Equal(1, result.Summary.Auto);
        Assert.Equal(0, result.Summary.Review);
        Assert.Equal(1, result.Summary.InvalidResponse);
        Assert.Equal(1, result.Summary.ByClass["bug"]);
    }

    [Fact]
    public async Task CatalogTravelsOnceInTheStateAndItemTextGoesWithItsQuestion()
    {
        var client = Client(("i0", "c0", 0.95));

        await new ClassifyService(client).JudgeAsync(new ClassifyRequest(
            [new(null, "crash on start")],
            TwoClasses,
            "triage"));

        var state = client.State!.AsObject();
        Assert.Equal("triage", state["purpose"]!.GetValue<string>());
        Assert.Equal(2, state["classes"]!.AsArray().Count);
        Assert.Equal("c0", state["classes"]![0]!["id"]!.GetValue<string>());
        Assert.Null(state["context"]);
    }

    [Fact]
    public async Task LimitsAreCheckedBeforeAnyModelCall()
    {
        var client = Client();
        var service = new ClassifyService(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.JudgeAsync(new ClassifyRequest([new(null, "x")], [TwoClasses[0]])));

        var tooManyItems = Enumerable.Range(0, Limits.MaxItems + 1)
            .Select(index => new TextItem(null, $"item {index}"))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.JudgeAsync(new ClassifyRequest(tooManyItems, TwoClasses)));

        // 64 items x 250 classes exceeds 8,000 pairs: the batch is rejected before it costs.
        var classes = Enumerable.Range(0, Limits.MaxClasses)
            .Select(index => new ClassDefinition(null, $"class {index}"))
            .ToArray();

        var items = Enumerable.Range(0, Limits.MaxItems)
            .Select(index => new TextItem(null, $"item {index}"))
            .ToArray();

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.JudgeAsync(new ClassifyRequest(items, classes)));

        Assert.Contains("item-class budget", error.Message, StringComparison.Ordinal);
        Assert.Null(client.Questions);
    }

    [Fact]
    public async Task LongItemTextIsTruncatedBeforeItIsSent()
    {
        var client = Client(("i0", "c0", 0.95));
        var text = new string('x', Limits.MaxItemChars + 100);

        await new ClassifyService(client).JudgeAsync(new ClassifyRequest([new(null, text)], TwoClasses));

        var sent = client.Questions!["i0"].Instructions.ToNode()["item"]!["text"]!.GetValue<string>();
        Assert.Equal(Excerpts.Truncate(text, Limits.MaxItemChars), sent);
        Assert.True(sent.Length < text.Length);
    }

    [Fact]
    public async Task OutputUsesSnakeCaseFieldNames()
    {
        var client = Client(("i0", "c0", 0.95));

        var result = await new ClassifyService(client).JudgeAsync(new ClassifyRequest(
            [new(null, "text")],
            TwoClasses));

        using var document = JsonDocument.Parse(JevJson.Serialize(result));
        var root = document.RootElement;

        Assert.Equal("jev_classify", root.GetProperty("tool").GetString());
        Assert.Equal(0.85, root.GetProperty("thresholds").GetProperty("auto_accept").GetDouble());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("by_class").GetProperty("bug").GetInt32());
        Assert.Equal(0.95, root.GetProperty("results")[0].GetProperty("top_probability").GetDouble());
        Assert.False(root.GetProperty("results")[0].TryGetProperty("status", out _));
    }
}
