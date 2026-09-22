using JevMcp.Core;
using JevMcp.Providers;
using JevMcp.Tools;
using System.Text.Json.Nodes;

namespace JevMcp.Tests.Mcp;

/// <summary>
/// Supplies plausible Jev answers for every playground tool when exercised through MCP HTTP.
/// </summary>
internal sealed class PlaygroundMcpFakeJevClient : IJevClient
{
    public JevUsage Usage { get; } = new(12, 4);

    public JevProviderKind Provider { get; } = JevProviderKind.TypeSafe;

    public string Model { get; } = "jev-latest";

    public Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
        foreach (var key in questions.Keys)
        {
            answers[key] = AnswerFor(key);
        }

        return Task.FromResult(new JevAskResult(answers, Usage, Provider, model ?? Model));
    }

    private static JevAnswer AnswerFor(string key) => key switch
    {
        "injection" => Noul(0.05),
        "substance" => Noul(0.9),
        "relevance" => Noul(0.85),
        "exists" => Noul(1),
        "safe_to_apply" => Noul(0.95),
        "best" => Choice("a", ("a", 0.9), ("b", 0.1)),
        "relationship" => Choice("same_fact", ("same_fact", 0.9), ("contradicts", 0.05), ("different_facts", 0.05)),
        "correctness" or "spec_match" => Score(2),
        "test_gap" or "blast_radius" => Score(0),
        _ when key.StartsWith("rel_", StringComparison.Ordinal) => Noul(0.85),
        _ when key.StartsWith("f", StringComparison.Ordinal) => Choice("c0", ("c0", 0.92), ("c1", 0.04), ("none_of_them", 0.04)),
        _ when key is "a" or "b" or "1" => Choice(key, (key, 0.88), ("other", 0.06), ("none_of_them", 0.06)),
        _ => Choice("c0", ("c0", 0.9), ("c1", 0.05), ("none_of_them", 0.05)),
    };

    private static JevAnswer Noul(double value) => new()
    {
        Kind = JevAnswerKind.Noul,
        Noul = value,
        Confidence = 0.9,
    };

    private static JevAnswer Score(double value) => new()
    {
        Kind = JevAnswerKind.Score,
        Score = value,
        Confidence = 0.9,
    };

    private static JevAnswer Choice(string choice, params (string Key, double Probability)[] probabilities) => new()
    {
        Kind = JevAnswerKind.Choice,
        Choice = choice,
        Confidence = 0.9,
        Probabilities = probabilities.ToDictionary(entry => entry.Key, entry => entry.Probability, StringComparer.Ordinal),
    };
}
