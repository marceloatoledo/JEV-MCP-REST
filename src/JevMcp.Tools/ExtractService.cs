using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JevMcp.Core;
using JevMcp.Providers;
using static System.FormattableString;

namespace JevMcp.Tools;

/// <summary>Field to extract: the regex finds the candidates, the model chooses which one counts.</summary>
public sealed record ExtractField(string Id, string Pattern, string Description, string? Flags = null);

public sealed record ExtractRequest(
    string Document,
    IReadOnlyList<ExtractField> Fields,
    string? Purpose = null,
    double? AutoAccept = null,
    double? MinimumMargin = null);

public sealed record ExtractFieldResult
{
    public required string Id { get; init; }

    public string? Value { get; init; }

    public required string Status { get; init; }

    public string? Reason { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Confidence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopProbability { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Margin { get; init; }

    public required int CandidatesConsidered { get; init; }

    public required bool CandidatesTruncated { get; init; }

    public required int MatchesSkippedTooLong { get; init; }
}

public sealed record ExtractSummary(int Fields, int Extracted, int Auto, int Review, int NotFound, int Invalid);

public sealed record ExtractResult
{
    public string Tool => JevTools.Extract;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public required ExtractSummary Summary { get; init; }

    public required ClassifyThresholds Thresholds { get; init; }

    public required IReadOnlyList<ExtractFieldResult> Results { get; init; }

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// Literal extraction: the returned value is always a substring of the document, chosen
/// among the matches of the caller's regex. The model selects, it never writes.
/// </summary>
public sealed partial class ExtractService
{
    /// <summary>Option the model uses to reject every candidate.</summary>
    public const string NoneOfThem = "none_of_them";

    private const string StatusNotFound = "not_found";
    private const string StatusInvalidPattern = "invalid_pattern";

    private readonly IJevClient _client;

    public ExtractService(IJevClient client)
    {
        _client = client;
    }

    public async Task<ExtractResult> JudgeAsync(ExtractRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Document))
        {
            throw new ArgumentException("document must not be empty.", nameof(request));
        }

        if (request.Document.Length > Limits.MaxExtractDocumentChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                Invariant($"document must be at most {Limits.MaxExtractDocumentChars:N0} characters."));
        }

        if (request.Fields.Count == 0)
        {
            throw new ArgumentException("fields must contain at least one field.", nameof(request));
        }

        if (request.Fields.Count > Limits.MaxExtractFields)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"fields must contain at most {Limits.MaxExtractFields} fields.");
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

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in request.Fields)
        {
            if (!FieldId().IsMatch(field.Id))
            {
                throw new ArgumentException(
                    $"Field id \"{field.Id}\" must match ^[a-z][a-z0-9_-]*$ and be at most 64 characters.",
                    nameof(request));
            }

            if (!seen.Add(field.Id))
            {
                throw new ArgumentException($"Duplicate field id: {field.Id}", nameof(request));
            }

            if (string.IsNullOrEmpty(field.Pattern) || field.Pattern.Length > 500)
            {
                throw new ArgumentException(
                    "each field pattern must be between 1 and 500 characters.",
                    nameof(request));
            }
        }

        var matched = request.Fields
            .Select((field, index) => new FieldMatches(
                field,
                $"f{index}",
                RegexRunner.Run(request.Document, field.Pattern, field.Flags)))
            .ToArray();

        var previewChars = matched.Sum(field => field.Matches.Candidates.Sum(candidate => candidate.Length));
        if (previewChars > Limits.MaxExtractTotalChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                Invariant($"Batch too large: {previewChars:N0} candidate characters exceeds the ") +
                Invariant($"{Limits.MaxExtractTotalChars:N0} character budget. Tighten the patterns or split the call."));
        }

        var questions = new OrderedDictionary<string, JevQuestion>(StringComparer.Ordinal);
        var stateFields = new JsonArray();

        foreach (var field in matched.Where(field => field.Asks))
        {
            var criteria = new List<ChoiceCriterion>(field.Matches.Candidates.Count + 1);
            criteria.AddRange(field.Matches.Candidates.Select((candidate, index) =>
                new ChoiceCriterion($"c{index}", $"Candidate value: {JsonSerializer.Serialize(candidate)}")));
            criteria.Add(new ChoiceCriterion(NoneOfThem, "None of the candidates is the value this field asks for"));

            questions[field.Key] = new ChoiceQuestion(
                $"Which candidate is the correct value of the field \"{field.Field.Id}\" ({field.Field.Description}) " +
                "in the document in the state? Pick the exact substring the document presents as this field's value.",
                criteria);

            stateFields.Add(new JsonObject
            {
                ["id"] = field.Key,
                ["description"] = field.Field.Description,
                ["pattern"] = field.Field.Pattern,
            });
        }

        // A field with no match never reaches the model, and a call where no field
        // matched never becomes a request: there is nothing to choose.
        var answer = questions.Count > 0
            ? await _client
                .AskAsync(
                    new JsonObject
                    {
                        ["purpose"] = request.Purpose,
                        ["document"] = request.Document,
                        ["fields"] = stateFields,
                    },
                    questions,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false)
            : null;

        var results = matched
            .Select(field => Judge(field, answer, autoAccept, minimumMargin))
            .ToArray();

        return new ExtractResult
        {
            Model = answer?.Model ?? JevProviderOptions.DefaultModel,
            Provider = answer is null ? "none" : JevWireNames.Of(answer.Provider),
            Summary = new ExtractSummary(
                Fields: results.Length,
                Extracted: results.Count(result => result.Value is not null),
                Auto: results.Count(result => result.Status == JevWireNames.Of(PolicyAction.Auto)),
                Review: results.Count(result => result.Status == JevWireNames.Of(PolicyAction.Review)),
                NotFound: results.Count(result => result.Status == StatusNotFound),
                Invalid: results.Count(result =>
                    result.Status is StatusInvalidPattern or JevWireNames.InvalidResponse)),
            Thresholds = new ClassifyThresholds(autoAccept, minimumMargin),
            Results = results,
            Usage = answer?.Usage ?? JevUsage.None,
        };
    }

    private static ExtractFieldResult Judge(
        FieldMatches field,
        JevAskResult? answer,
        double autoAccept,
        double minimumMargin)
    {
        var candidates = field.Matches.Candidates;

        // An incomplete universe contaminates every outcome, including the negative one:
        // the right value may be among the matches we did not send.
        var incomplete = field.Matches.Truncated || field.Matches.TooLong > 0;

        if (field.Matches.Error is { } error)
        {
            return Base(field, StatusInvalidPattern, 0) with { Reason = error };
        }

        if (candidates.Count == 0)
        {
            return field.Matches.TooLong > 0
                ? Base(field, JevWireNames.Of(PolicyAction.Review), 0) with { Reason = "matches_too_long" }
                : Base(field, StatusNotFound, 0) with { Reason = "no_regex_matches" };
        }

        var expected = candidates.Select((_, index) => $"c{index}").Append(NoneOfThem);
        var choice = Answers.ValidateChoice(answer?.Answers.GetValueOrDefault(field.Key), expected);

        if (choice is null)
        {
            return Base(field, JevWireNames.InvalidResponse, candidates.Count);
        }

        var margin = Policy.Margin(choice.Probabilities);
        var topProbability = choice.Probabilities[choice.Choice];
        var scored = Base(field, string.Empty, candidates.Count) with
        {
            Confidence = choice.Confidence,
            TopProbability = topProbability,
            Margin = margin,
        };

        var decision = Policy.ClassificationDecision(topProbability, margin, autoAccept, minimumMargin);

        if (choice.Choice == NoneOfThem)
        {
            if (incomplete)
            {
                return scored with
                {
                    Status = JevWireNames.Of(PolicyAction.Review),
                    Reason = "candidate_limit",
                };
            }

            // The negative answer goes through the same bar as the positive one: "none of them"
            // only becomes a definitive absence when the model is confident and the margin is clear.
            return decision == PolicyAction.Auto
                ? scored with { Status = StatusNotFound, Reason = "none_matched" }
                : scored with { Status = JevWireNames.Of(PolicyAction.Review), Reason = "none_matched_ambiguous" };
        }

        return scored with
        {
            Value = candidates[int.Parse(choice.Choice[1..], System.Globalization.CultureInfo.InvariantCulture)],
            Status = JevWireNames.Of(incomplete ? PolicyAction.Review : decision),
            Reason = incomplete ? "candidate_limit" : null,
        };
    }

    private static ExtractFieldResult Base(FieldMatches field, string status, int considered) => new()
    {
        Id = field.Field.Id,
        Value = null,
        Status = status,
        Reason = null,
        CandidatesConsidered = considered,
        CandidatesTruncated = field.Matches.Truncated,
        MatchesSkippedTooLong = field.Matches.TooLong,
    };

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,63}$")]
    private static partial Regex FieldId();

    private sealed record FieldMatches(ExtractField Field, string Key, RegexMatches Matches)
    {
        public bool Asks => Matches.Error is null && Matches.Candidates.Count > 0;
    }
}
