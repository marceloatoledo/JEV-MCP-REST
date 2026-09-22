using System.Text.Json.Nodes;
using JevMcp.Core;
using JevMcp.Providers;

namespace JevMcp.Tests.Tools;

/// <summary>
/// Fake client that returns scripted answers and records what was asked.
/// Tool tests cover question assembly and policy, not transport.
/// </summary>
internal sealed class FakeJevClient : IJevClient
{
    private readonly IReadOnlyDictionary<string, JevAnswer> _answers;

    public FakeJevClient(IReadOnlyDictionary<string, JevAnswer> answers)
    {
        _answers = answers;
    }

    public JsonNode? State { get; private set; }

    public IReadOnlyDictionary<string, JevQuestion>? Questions { get; private set; }

    public JevUsage Usage { get; init; } = new(11, 3);

    public JevProviderKind Provider { get; init; } = JevProviderKind.TypeSafe;

    public string Model { get; init; } = "jev-latest";

    public Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        State = state;
        Questions = questions;

        return Task.FromResult(new JevAskResult(_answers, Usage, Provider, model ?? Model));
    }
}

/// <summary>Client that always fails, to cover the MCP shell error path.</summary>
internal sealed class ThrowingJevClient : IJevClient
{
    private readonly Func<Exception> _failure;

    public ThrowingJevClient(Func<Exception> failure)
    {
        _failure = failure;
    }

    public Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        throw _failure();
    }
}

/// <summary>Shortcuts to build model answers in tests.</summary>
internal static class FakeAnswers
{
    public static JevAnswer Noul(double value, double? confidence = null) => new()
    {
        Kind = JevAnswerKind.Noul,
        Noul = value,
        Confidence = confidence,
    };

    public static JevAnswer Choice(string choice, double? confidence, params (string Key, double Probability)[] probabilities) => new()
    {
        Kind = JevAnswerKind.Choice,
        Choice = choice,
        Confidence = confidence,
        Probabilities = probabilities.ToDictionary(entry => entry.Key, entry => entry.Probability, StringComparer.Ordinal),
    };
}
