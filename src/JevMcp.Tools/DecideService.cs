using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JevMcp.Core;
using JevMcp.Providers;
using static System.FormattableString;

namespace JevMcp.Tools;

/// <summary>Alternative in a decision. The id is a slug and comes back verbatim in the result.</summary>
public sealed record DecideCandidate(string Id, string Description);

/// <summary>A bounded decision, with explicit evidence and priorities.</summary>
public sealed record DecideRequest(
    string Decision,
    string Evidence,
    string Priorities,
    IReadOnlyList<DecideCandidate> Candidates,
    IReadOnlyList<string>? Requirements = null,
    bool? EscapeHatches = null);

public sealed record DecideRecommendation
{
    public string? Selected { get; init; }

    public bool? Escaped { get; init; }

    public double? Confidence { get; init; }

    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }
}

/// <summary>Check of a requirement against a candidate, by requirement index.</summary>
public sealed record DecideCheck(string Candidate, int Requirement, string Answer);

public sealed record DecideResult
{
    public string Tool => JevTools.Decide;

    public required string Model { get; init; }

    public required string Provider { get; init; }

    public required DecideRecommendation Recommendation { get; init; }

    public required int RequirementsChecked { get; init; }

    public required IReadOnlyList<DecideCheck> Checks { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>auto | review | escalate — whether the host can act on the recommendation alone.</summary>
    public required string Action { get; init; }

    public required JevUsage Usage { get; init; }
}

/// <summary>
/// Decision among bounded alternatives: one Choice over the candidates plus escape hatches,
/// and an independent check per requirement and candidate, all in one request.
/// </summary>
public sealed partial class DecideService
{
    /// <summary>Character limit of the decision statement.</summary>
    public const int MaxDecisionChars = 1_500;

    /// <summary>Character limit of the evidence.</summary>
    public const int MaxEvidenceChars = 12_000;

    /// <summary>Character limit of the priorities.</summary>
    public const int MaxPrioritiesChars = 2_000;

    /// <summary>Character limit of a candidate description.</summary>
    public const int MaxCandidateDescriptionChars = 2_000;

    /// <summary>Character limit of a requirement.</summary>
    public const int MaxRequirementChars = 500;

    private static readonly ChoiceCriterion[] RequirementCriteria =
    [
        new("supported", "The evidence and mechanism support this specific requirement"),
        new("contradicted",
            "The evidence or mechanism contradicts this specific requirement, not merely another requirement"),
        new("unknown", "Relevant evidence is missing; neither satisfaction nor violation is established"),
    ];

    private readonly IJevClient _client;

    public DecideService(IJevClient client)
    {
        _client = client;
    }

    public async Task<DecideResult> JudgeAsync(DecideRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Bounded(request.Decision, MaxDecisionChars, "decision");
        Bounded(request.Evidence, MaxEvidenceChars, "evidence");
        Bounded(request.Priorities, MaxPrioritiesChars, "priorities");

        if (request.Candidates.Count < 2 || request.Candidates.Count > Limits.MaxDecideCandidates)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"candidates must contain between 2 and {Limits.MaxDecideCandidates} candidates.");
        }

        var requirements = request.Requirements ?? [];
        if (requirements.Count > Limits.MaxRequirements)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"requirements must contain at most {Limits.MaxRequirements} requirements.");
        }

        foreach (var requirement in requirements)
        {
            Bounded(requirement, MaxRequirementChars, "requirements");
        }

        var includeHatches = request.EscapeHatches ?? true;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in request.Candidates)
        {
            if (!CandidateId().IsMatch(candidate.Id))
            {
                throw new ArgumentException(
                    $"Candidate id \"{candidate.Id}\" must match ^[a-z][a-z0-9_-]*$ and be at most 64 characters.",
                    nameof(request));
            }

            Bounded(candidate.Description, MaxCandidateDescriptionChars, "candidates");

            if (!seen.Add(candidate.Id))
            {
                throw new ArgumentException($"Duplicate candidate id: {candidate.Id}", nameof(request));
            }

            // A candidate named like an escape hatch would make the result ambiguous: there
            // is no way to tell whether the model chose the alternative or declined the choice.
            if (includeHatches && Criteria.DecideEscapeHatches.ContainsKey(candidate.Id))
            {
                throw new ArgumentException(
                    $"Candidate id \"{candidate.Id}\" collides with an escape hatch; rename it or set escape_hatches: false.",
                    nameof(request));
            }
        }

        var candidates = Keyed.Map(
            request.Candidates,
            "candidate",
            "option_",
            candidate => candidate.Id,
            candidate => candidate.Description);

        var criteria = new List<ChoiceCriterion>(candidates.Count + Criteria.DecideEscapeHatches.Count);
        criteria.AddRange(candidates.Select(candidate => new ChoiceCriterion(candidate.Key, candidate.Text)));

        if (includeHatches)
        {
            criteria.AddRange(Criteria.DecideEscapeHatches.Select(hatch =>
                new ChoiceCriterion(hatch.Key, hatch.Value)));
        }

        var questions = new OrderedDictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["recommendation"] = new ChoiceQuestion(
                "Which candidate best fits the decision, evidence, and priorities? " +
                (includeHatches ? "Select a candidate or an escape hatch. " : string.Empty) +
                "Do not invent missing facts, preferences, or approvals.",
                criteria),
        };

        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            for (var requirementIndex = 0; requirementIndex < requirements.Count; requirementIndex++)
            {
                questions[$"check_{candidateIndex}_{requirementIndex}"] = new ChoiceQuestion(
                    $"How does the mechanism in candidates[{candidateIndex}] relate to requirements[{requirementIndex}], " +
                    "using the evidence? Judge only this property, not the candidate overall desirability. " +
                    "Missing evidence is not contradiction.",
                    RequirementCriteria);
            }
        }

        var candidateState = new JsonArray();
        foreach (var candidate in candidates)
        {
            candidateState.Add(new JsonObject { ["id"] = candidate.Key, ["description"] = candidate.Text });
        }

        var requirementState = new JsonArray();
        foreach (var requirement in requirements)
        {
            requirementState.Add(requirement);
        }

        var state = new JsonObject
        {
            ["decision"] = request.Decision,
            ["evidence"] = request.Evidence,
            ["priorities"] = request.Priorities,
            ["candidates"] = candidateState,
            ["requirements"] = requirementState,
        };

        var answer = await _client.AskAsync(state, questions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var recommendationKeys = candidates
            .Select(candidate => candidate.Key)
            .Concat(includeHatches ? Criteria.DecideEscapeHatches.Keys : [])
            .ToArray();

        var recommendation = Answers.ValidateChoice(
            answer.Answers.GetValueOrDefault("recommendation"),
            recommendationKeys);

        var byKey = candidates.ToDictionary(candidate => candidate.Key, StringComparer.Ordinal);
        var checkKeys = RequirementCriteria.Select(criterion => criterion.Key).ToArray();
        var checks = new List<DecideCheck>(candidates.Count * requirements.Count);

        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            for (var requirementIndex = 0; requirementIndex < requirements.Count; requirementIndex++)
            {
                var check = Answers.ValidateChoice(
                    answer.Answers.GetValueOrDefault($"check_{candidateIndex}_{requirementIndex}"),
                    checkKeys);

                checks.Add(new DecideCheck(
                    candidates[candidateIndex].External,
                    requirementIndex,
                    check?.Choice ?? JevWireNames.InvalidResponse));
            }
        }

        var escaped = recommendation is not null && !byKey.ContainsKey(recommendation.Choice);
        var selected = recommendation is null
            ? null
            : escaped ? recommendation.Choice : byKey[recommendation.Choice].External;

        // Disagreement between a requirement check and the recommendation is signal, not
        // noise: independent questions may disagree, and that is what the operator needs to see.
        var contradicted = selected is not null && !escaped
            ? Policy.ContradictsRecommendation(
                checks
                    .Where(check => check.Answer != JevWireNames.InvalidResponse)
                    .Select(check => new RequirementCheck(
                        check.Candidate,
                        check.Requirement,
                        Enum.Parse<RequirementVerdict>(check.Answer, ignoreCase: true))),
                selected)
            : [];

        IReadOnlyList<string> warnings = contradicted.Count > 0
            ?
            [
                $"Requirement{(contradicted.Count > 1 ? "s" : string.Empty)} " +
                $"{string.Join(", ", contradicted.Select(index => index + 1))} " +
                "contradicted by the recommended candidate; inspect before acting",
            ]
            : Array.Empty<string>();

        var decision = Policy.DecideAction(
            recommendation is null,
            escaped,
            warnings.Count > 0,
            recommendation?.Confidence);

        return new DecideResult
        {
            Model = answer.Model,
            Provider = JevWireNames.Of(answer.Provider),
            Recommendation = recommendation is null
                ? new DecideRecommendation { Status = JevWireNames.InvalidResponse }
                : new DecideRecommendation
                {
                    Selected = selected,
                    Escaped = escaped,
                    Confidence = recommendation.Confidence,
                    Probabilities = recommendation.Probabilities.ToDictionary(
                        entry => byKey.TryGetValue(entry.Key, out var candidate) ? candidate.External : entry.Key,
                        entry => entry.Value,
                        StringComparer.Ordinal),
                },
            RequirementsChecked = requirements.Count,
            Checks = checks,
            Warnings = warnings,
            Action = JevWireNames.Of(decision),
            Usage = answer.Usage,
        };
    }

    private static void Bounded(string value, int maximum, string field)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"{field} must not be empty.", field);
        }

        if (value.Length > maximum)
        {
            throw new ArgumentOutOfRangeException(
                field,
                Invariant($"{field} must be at most {maximum:N0} characters."));
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,63}$")]
    private static partial Regex CandidateId();
}
