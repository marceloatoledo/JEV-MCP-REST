using System.Text.Json.Nodes;

namespace JevMcp.Providers;

/// <summary>
/// Instructions for a question. Accepts plain text or a JSON structure, because
/// System One also accepts a structured rubric in place of a sentence.
/// </summary>
public readonly record struct JevInstructions
{
    private readonly JsonNode? _value;

    private JevInstructions(JsonNode value) => _value = value;

    public static JevInstructions Text(string text) => new(JsonValue.Create(text));

    public static JevInstructions Structured(JsonNode node) => new(node);

    public static implicit operator JevInstructions(string text) => Text(text);

    internal JsonNode ToNode() => _value?.DeepClone() ?? JsonValue.Create(string.Empty);
}

/// <summary>A Choice option. Description is optional; list order is the order in the question.</summary>
public sealed record ChoiceCriterion(string Key, string? Description = null);

/// <summary>Question sent to Jev in one of the three judgment primitives.</summary>
public abstract record JevQuestion(JevInstructions Instructions)
{
    /// <summary>Type name in the System One contract.</summary>
    public abstract string WireType { get; }
}

/// <summary>Probability that a proposition is true.</summary>
public sealed record NoulQuestion(
    JevInstructions Instructions,
    string? TrueMeaning = null,
    string? FalseMeaning = null) : JevQuestion(Instructions)
{
    public override string WireType => "noul";
}

/// <summary>Choice of one option among named criteria, with a probability distribution.</summary>
public sealed record ChoiceQuestion(
    JevInstructions Instructions,
    IReadOnlyList<ChoiceCriterion> Criteria) : JevQuestion(Instructions)
{
    public override string WireType => "choice";
}

/// <summary>Level in an ordered rubric. The returned score is the level index.</summary>
public sealed record ScoreQuestion(
    JevInstructions Instructions,
    IReadOnlyList<string> Levels) : JevQuestion(Instructions)
{
    public override string WireType => "score";
}
