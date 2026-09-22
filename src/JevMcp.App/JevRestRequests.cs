using System.ComponentModel;
using System.Text.Json;

namespace JevMcp.App;

internal sealed class VerifyApiRequest
{
    [Description("Claims to verify, e.g. individual factual statements from a report.")]
    public required string[] Claims { get; init; }

    [Description("Evidence: a document as text, one {id?, text} item, or an array of such items.")]
    public JsonElement Evidence { get; init; }

    [Description("Verdicts at or above this confidence stand automatically. Default 0.8.")]
    public double? AutoAccept { get; init; }
}

internal sealed class ScreenApiRequest
{
    [Description("The content to screen, e.g. a fetched web page or pasted document.")]
    public required string Text { get; init; }

    [Description("What the consuming agent is trying to do; enables relevance and 'skip'.")]
    public string? Purpose { get; init; }

    [Description("Injection probability at or above which content is blocked. Default 0.75.")]
    public double? BlockAt { get; init; }

    [Description("Injection probability at or above which content is flagged for review. Default 0.25.")]
    public double? ReviewAt { get; init; }
}

internal sealed class FindApiRequest
{
    [Description("What you are looking for, in natural language.")]
    public required string Query { get; init; }

    [Description("Candidates, each with optional id and a text field. Up to 250; texts truncated at 2000 chars.")]
    public JsonElement Candidates { get; init; }

    [Description("How many ranked candidates to return. Default 5.")]
    public int? TopK { get; init; }
}

internal sealed class RerankApiRequest
{
    [Description("What relevance is measured against, in natural language.")]
    public required string Query { get; init; }

    [Description("Candidates, each with optional id and a text field. Up to 250; texts truncated at 2000 chars.")]
    public JsonElement Candidates { get; init; }

    [Description("How many ranked candidates to return. Default: all.")]
    public int? TopK { get; init; }
}

internal sealed class ClassifyApiRequest
{
    [Description("Items to classify, each with optional id and a text field. Up to 64 per call.")]
    public JsonElement Items { get; init; }

    [Description("Shared class catalog, each entry with optional id and a description.")]
    public JsonElement Classes { get; init; }

    [Description("What this classification is for; shared across all items.")]
    public string? Purpose { get; init; }

    [Description("Shared context available to every item's judgment.")]
    public JsonElement? Context { get; init; }

    [Description("Minimum top probability for auto. Default 0.85.")]
    public double? AutoAccept { get; init; }

    [Description("Minimum winner-to-runner-up gap for auto. Default 0.5.")]
    public double? MinimumMargin { get; init; }
}

internal sealed class DecideApiRequest
{
    [Description("The bounded decision to make.")]
    public required string Decision { get; init; }

    [Description("Facts and measurements, not opinions.")]
    public required string Evidence { get; init; }

    [Description("Explicit preferences and constraints from the user or plan.")]
    public required string Priorities { get; init; }

    [Description("The alternatives, 2 to 6, each with slug id and description.")]
    public JsonElement Candidates { get; init; }

    [Description("Specific requirements to check per candidate, up to 3.")]
    public string[]? Requirements { get; init; }

    [Description("Include ask_user / investigate / none as choosable options. Default true.")]
    public bool? EscapeHatches { get; init; }
}

internal sealed class CompareApiRequest
{
    [Description("First passage. Rejected above 20,000 characters.")]
    public required string PassageA { get; init; }

    [Description("Second passage. Rejected above 20,000 characters.")]
    public required string PassageB { get; init; }

    [Description("Named aspects to judge independently. Each tests one property.")]
    public string[]? Aspects { get; init; }

    [Description("What this comparison is for; helps disambiguate overlap.")]
    public string? Purpose { get; init; }

    [Description("Minimum top probability for auto. Default 0.85.")]
    public double? AutoAccept { get; init; }

    [Description("Minimum winner-to-runner-up gap for auto. Default 0.5.")]
    public double? MinimumMargin { get; init; }
}

internal sealed class ExtractApiRequest
{
    [Description("The document to extract from. Rejected above 50,000 characters.")]
    public required string Document { get; init; }

    [Description("Fields to extract, up to 32, each with id, pattern, description, and optional flags.")]
    public JsonElement Fields { get; init; }

    [Description("What the extraction is for; shared across fields.")]
    public string? Purpose { get; init; }

    [Description("Minimum top probability for auto. Default 0.85.")]
    public double? AutoAccept { get; init; }

    [Description("Minimum winner-to-runner-up gap for auto. Default 0.5.")]
    public double? MinimumMargin { get; init; }
}

internal sealed class ReviewApiRequest
{
    [Description("What the user asked for; this frames the review, it is not proof.")]
    public required string Request { get; init; }

    [Description("Proposed patch, file excerpt, or change summary. Truncated at 50,000 chars.")]
    public required string Diff { get; init; }

    [Description("Reported test output, if any.")]
    public string? Tests { get; init; }

    [Description("safe_to_apply and min score confidence at or above this may stand automatically. Default 0.8.")]
    public double? AutoAccept { get; init; }

    [Description("Min score confidence or safe_to_apply below this escalates. Default min(0.5, auto_accept).")]
    public double? ReviewAt { get; init; }

    [Description("Weighted composite at or above this is required for auto. Default 0.7.")]
    public double? CompositeFloor { get; init; }
}

internal sealed class GateApiRequest
{
    [Description("What the user asked for; this is not evidence of completion.")]
    public required string Request { get; init; }

    [Description("Proposed patch, file excerpt, or change summary. Truncated at 50,000 chars.")]
    public required string Diff { get; init; }

    [Description("Completion claims to check against evidence. Up to 16 per call.")]
    public required string[] Claims { get; init; }

    [Description("Evidence: a document as text, one {id?, text} item, or an array of such items.")]
    public JsonElement Evidence { get; init; }

    [Description("Reported test output for the patch review.")]
    public string? Tests { get; init; }

    [Description("Review and per-claim confidence at or above this may stand automatically. Default 0.8.")]
    public double? AutoAccept { get; init; }

    [Description("Score, safe_to_apply, or per-claim confidence below this escalates.")]
    public double? ReviewAt { get; init; }

    [Description("Weighted composite at or above this is required for auto. Default 0.7.")]
    public double? CompositeFloor { get; init; }
}

internal sealed record JevRestToolInfo(string Tool, string Method, string Path, string Title, string Description);

internal sealed record HealthStatusResponse(string Status, string? Provider, string? Model, string? Error);
