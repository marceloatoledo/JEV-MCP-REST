using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JevMcp.Core;
using JevMcp.Providers;
using static System.FormattableString;

namespace JevMcp.Tools;

/// <summary>Two passages to compare, with optional aspects judged separately.</summary>
public sealed record CompareRequest(
    string PassageA,
    string PassageB,
    IReadOnlyList<string>? Aspects = null,
    string? Purpose = null,
    double? AutoAccept = null,
    double? MinimumMargin = null);

public record CompareJudgment
{
    public string? Relation { get; init; }

    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    public double? Confidence { get; init; }

    public double? Margin { get; init; }

    public required string Decision { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
}

public sealed record CompareAspectJudgment : CompareJudgment
{
    public required string Aspect { get; init; }
}

public sealed record CompareResult
{
    public string Tool => JevTools.Compare;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public required CompareJudgment Overall { get; init; }

    public required IReadOnlyList<CompareAspectJudgment> Aspects { get; init; }

    public required ClassifyThresholds Thresholds { get; init; }

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// Comparison of two passages: an overall relation and, optionally, an independent
/// judgment per aspect. Without external evidence, agreeing is not being true.
/// </summary>
public sealed class CompareService
{
    /// <summary>Character limit of an aspect.</summary>
    public const int MaxAspectChars = 200;

    private static readonly ChoiceCriterion[] OverallCriteria =
        [.. Criteria.CompareRelations.Select(entry => new ChoiceCriterion(entry.Key, entry.Value))];

    private static readonly ChoiceCriterion[] AspectCriteria =
        [.. Criteria.AspectRelations.Select(entry => new ChoiceCriterion(entry.Key, entry.Value))];

    private readonly IJevClient _client;

    public CompareService(IJevClient client)
    {
        _client = client;
    }

    public async Task<CompareResult> JudgeAsync(CompareRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Passage(request.PassageA, "passage_a");
        Passage(request.PassageB, "passage_b");

        var aspects = request.Aspects ?? [];
        if (aspects.Count > Limits.MaxCompareAspects)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"aspects must contain at most {Limits.MaxCompareAspects} aspects.");
        }

        foreach (var aspect in aspects)
        {
            if (string.IsNullOrEmpty(aspect) || aspect.Length > MaxAspectChars)
            {
                throw new ArgumentException(
                    $"each aspect must be between 1 and {MaxAspectChars} characters.",
                    nameof(request));
            }
        }

        var autoAccept = Thresholds.Unit(
            request.AutoAccept,
            ClassifyService.DefaultAutoAccept,
            "auto_accept",
            nameof(request));

        var minimumMargin = Thresholds.Unit(
            request.MinimumMargin,
            ClassifyService.DefaultMinimumMargin,
            "minimum_margin",
            nameof(request));

        var questions = new OrderedDictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["overall"] = new ChoiceQuestion(
                "Do the two passages state the same underlying fact, contradict each other, or discuss different facts?",
                OverallCriteria),
        };

        for (var index = 0; index < aspects.Count; index++)
        {
            questions[$"aspect_{index}"] = new ChoiceQuestion(
                $"Judging only the aspect \"{aspects[index]}\" of the two passages in the state, which relation holds?",
                AspectCriteria);
        }

        var aspectState = new JsonArray();
        foreach (var aspect in aspects)
        {
            aspectState.Add(aspect);
        }

        var state = new JsonObject
        {
            ["purpose"] = request.Purpose,
            ["passage_a"] = request.PassageA,
            ["passage_b"] = request.PassageB,
            ["aspects"] = aspectState,
        };

        var answer = await _client.AskAsync(state, questions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var expected = Criteria.CompareRelations.Keys.ToArray();

        var overall = Judge(answer.Answers.GetValueOrDefault("overall"), expected, autoAccept, minimumMargin);
        var aspectResults = aspects
            .Select((aspect, index) =>
            {
                var judgment = Judge(
                    answer.Answers.GetValueOrDefault($"aspect_{index}"),
                    expected,
                    autoAccept,
                    minimumMargin);

                return new CompareAspectJudgment
                {
                    Aspect = aspect,
                    Relation = judgment.Relation,
                    Probabilities = judgment.Probabilities,
                    Confidence = judgment.Confidence,
                    Margin = judgment.Margin,
                    Decision = judgment.Decision,
                    Status = judgment.Status,
                };
            })
            .ToArray();

        return new CompareResult
        {
            Model = answer.Model,
            Provider = JevWireNames.Of(answer.Provider),
            Overall = overall,
            Aspects = aspectResults,
            Thresholds = new ClassifyThresholds(autoAccept, minimumMargin),
            Usage = answer.Usage,
        };
    }

    private static CompareJudgment Judge(
        JevAnswer? raw,
        string[] expected,
        double autoAccept,
        double minimumMargin)
    {
        var choice = Answers.ValidateChoice(raw, expected);

        if (choice is null)
        {
            return new CompareJudgment
            {
                Relation = null,
                Probabilities = null,
                Confidence = null,
                Margin = null,
                Decision = JevWireNames.Of(PolicyAction.Review),
                Status = JevWireNames.InvalidResponse,
            };
        }

        var margin = Policy.Margin(choice.Probabilities);

        return new CompareJudgment
        {
            Relation = choice.Choice,
            Probabilities = choice.Probabilities,
            Confidence = choice.Confidence,
            Margin = margin,
            Decision = JevWireNames.Of(Policy.ClassificationDecision(
                choice.Probabilities[choice.Choice],
                margin,
                autoAccept,
                minimumMargin)),
        };
    }

    private static void Passage(string passage, string field)
    {
        if (string.IsNullOrEmpty(passage))
        {
            throw new ArgumentException($"{field} must not be empty.", field);
        }

        // A passage above the cap is rejected, not truncated: comparing half of a text
        // would answer a different question, and the caller would not know that.
        if (passage.Length > Limits.MaxComparePassageChars)
        {
            throw new ArgumentOutOfRangeException(
                field,
                Invariant($"{field} must be at most {Limits.MaxComparePassageChars:N0} characters."));
        }
    }
}
