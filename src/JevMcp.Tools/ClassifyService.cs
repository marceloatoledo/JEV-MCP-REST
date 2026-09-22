using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JevMcp.Core;
using JevMcp.Providers;
using static System.FormattableString;

namespace JevMcp.Tools;

/// <summary>Class of the shared catalog. The description is what carries the decision.</summary>
public sealed record ClassDefinition(string? Id, string Description);

/// <summary>Items to classify against a catalog sent a single time.</summary>
public sealed record ClassifyRequest(
    IReadOnlyList<TextItem> Items,
    IReadOnlyList<ClassDefinition> Classes,
    string? Purpose = null,
    JsonNode? Context = null,
    double? AutoAccept = null,
    double? MinimumMargin = null);

public sealed record ClassifyItemResult
{
    public required string Id { get; init; }

    public string? Classification { get; init; }

    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    public double? Confidence { get; init; }

    public double? Margin { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopProbability { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    public required string Decision { get; init; }
}

public sealed record ClassifySummary(
    int Items,
    int Auto,
    int Review,
    int InvalidResponse,
    IReadOnlyDictionary<string, int> ByClass);

public sealed record ClassifyThresholds(double AutoAccept, double MinimumMargin);

public sealed record ClassifyResult
{
    public string Tool => JevTools.Classify;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public required ClassifySummary Summary { get; init; }

    public required ClassifyThresholds Thresholds { get; init; }

    public required IReadOnlyList<ClassifyItemResult> Results { get; init; }

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// Batch classification: the catalog goes once in the state and each item becomes an
/// independent Choice, so the request grows with items + classes, not with the product.
/// </summary>
public sealed class ClassifyService
{
    /// <summary>Minimum top probability for automatic accept.</summary>
    public const double DefaultAutoAccept = 0.85;

    /// <summary>Minimum distance to the runner-up for automatic accept.</summary>
    public const double DefaultMinimumMargin = 0.5;

    private readonly IJevClient _client;

    public ClassifyService(IJevClient client)
    {
        _client = client;
    }

    public async Task<ClassifyResult> JudgeAsync(
        ClassifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Items.Count == 0)
        {
            throw new ArgumentException("items must contain at least one item.", nameof(request));
        }

        if (request.Items.Count > Limits.MaxItems)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"items must contain at most {Limits.MaxItems} items.");
        }

        if (request.Classes.Count < 2)
        {
            throw new ArgumentException("classes must contain at least two classes.", nameof(request));
        }

        if (request.Classes.Count > Limits.MaxClasses)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"classes must contain at most {Limits.MaxClasses} classes.");
        }

        var autoAccept = Thresholds.Unit(request.AutoAccept, DefaultAutoAccept, "auto_accept", nameof(request));
        var minimumMargin = Thresholds.Unit(
            request.MinimumMargin,
            DefaultMinimumMargin,
            "minimum_margin",
            nameof(request));

        // The caller's id comes back intact in the result; the key that goes to the model
        // is positional and opaque, so sanitization never renames anything visible. A
        // duplicate id is an error instead of a silent suffix: the caller needs to know it collided.
        var items = Keyed.Map(
            request.Items,
            "item",
            "i",
            item => item.Id,
            item => Excerpts.Truncate(item.Text, Limits.MaxItemChars));

        var classes = Keyed.Map(
            request.Classes,
            "class",
            "c",
            definition => definition.Id,
            definition => Excerpts.Truncate(definition.Description, Limits.MaxItemChars));

        if (items.Count * classes.Count > Limits.MaxClassifyItemClassBudget)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                Invariant($"Batch too large: {items.Count} items x {classes.Count} classes exceeds the ") +
                Invariant($"{Limits.MaxClassifyItemClassBudget:N0} item-class budget. Split the batch."));
        }

        var criteria = classes.Select(entry => new ChoiceCriterion(entry.Key)).ToArray();
        var questions = new OrderedDictionary<string, JevQuestion>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            questions[item.Key] = new ChoiceQuestion(
                JevInstructions.Structured(new JsonObject
                {
                    ["task"] = "Which class does this item belong to?",
                    ["item"] = new JsonObject { ["id"] = item.Key, ["text"] = item.Text },
                }),
                criteria);
        }

        var catalog = new JsonArray();
        foreach (var entry in classes)
        {
            catalog.Add(new JsonObject { ["id"] = entry.Key, ["description"] = entry.Text });
        }

        var state = new JsonObject
        {
            ["purpose"] = request.Purpose ?? "Assign each item to exactly one class.",
            ["context"] = request.Context?.DeepClone(),
            ["classes"] = catalog,
        };

        var answer = await _client.AskAsync(state, questions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var expectedKeys = classes.Select(entry => entry.Key).ToArray();
        var results = items
            .Select(item => Judge(item, classes, answer, expectedKeys, autoAccept, minimumMargin))
            .ToArray();

        var byClass = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var result in results.Where(result => result.Classification is not null))
        {
            byClass[result.Classification!] = byClass.GetValueOrDefault(result.Classification!) + 1;
        }

        return new ClassifyResult
        {
            Model = answer.Model,
            Provider = JevWireNames.Of(answer.Provider),
            Summary = new ClassifySummary(
                Items: results.Length,
                Auto: results.Count(result => result.Decision == JevWireNames.Of(PolicyAction.Auto)),
                Review: results.Count(result =>
                    result.Decision == JevWireNames.Of(PolicyAction.Review) && result.Status is null),
                InvalidResponse: results.Count(result => result.Status is not null),
                ByClass: byClass),
            Thresholds = new ClassifyThresholds(autoAccept, minimumMargin),
            Results = results,
            Usage = answer.Usage,
        };
    }

    private static ClassifyItemResult Judge(
        KeyedEntry item,
        IReadOnlyList<KeyedEntry> classes,
        JevAskResult answer,
        string[] expectedKeys,
        double autoAccept,
        double minimumMargin)
    {
        var choice = Answers.ValidateChoice(answer.Answers.GetValueOrDefault(item.Key), expectedKeys);

        if (choice is null)
        {
            return new ClassifyItemResult
            {
                Id = item.External,
                Status = JevWireNames.InvalidResponse,
                Classification = null,
                Probabilities = null,
                Confidence = null,
                Margin = null,
                Decision = JevWireNames.Of(PolicyAction.Review),
            };
        }

        var margin = Policy.Margin(choice.Probabilities);
        var topProbability = choice.Probabilities[choice.Choice];

        // Probabilities come back with the caller's ids: the opaque key does not leak.
        var probabilities = classes.ToDictionary(
            entry => entry.External,
            entry => choice.Probabilities.GetValueOrDefault(entry.Key),
            StringComparer.Ordinal);

        return new ClassifyItemResult
        {
            Id = item.External,
            Classification = classes.First(entry => entry.Key == choice.Choice).External,
            Probabilities = probabilities,
            Confidence = choice.Confidence,
            Margin = margin,
            TopProbability = topProbability,
            Decision = JevWireNames.Of(
                Policy.ClassificationDecision(topProbability, margin, autoAccept, minimumMargin)),
        };
    }
}
