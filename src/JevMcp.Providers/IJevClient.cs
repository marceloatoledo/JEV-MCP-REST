using System.Text.Json.Nodes;
using JevMcp.Core;

namespace JevMcp.Providers;

/// <summary>Where the Jev model is hosted.</summary>
public enum JevProviderKind
{
    TypeSafe,
    OpenRouter,
    Cloudflare,
    Vercel,
    Compatible,
}

/// <summary>Token consumption reported by the provider.</summary>
public sealed record JevUsage(int InputTokens, int OutputTokens)
{
    public static JevUsage None { get; } = new(0, 0);
}

/// <summary>
/// Result of a query. Answers come keyed by question id, with no validity judgment:
/// each tool applies its own invalid-response contract.
/// </summary>
public sealed record JevAskResult(
    IReadOnlyDictionary<string, JevAnswer> Answers,
    JevUsage Usage,
    JevProviderKind Provider,
    string Model);

/// <summary>
/// The only way to ask Jev. Tools do not know which provider is serving the request.
/// </summary>
public interface IJevClient
{
    /// <summary>
    /// Sends state and questions in a single request.
    /// </summary>
    /// <param name="state">Context to judge: text, object, or list.</param>
    /// <param name="questions">Questions keyed by id.</param>
    /// <param name="model">Model to use; when null, the configured one.</param>
    Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Missing or inconsistent provider configuration. This is not a network failure.</summary>
public sealed class JevConfigurationException : Exception
{
    public JevConfigurationException(string message) : base(message)
    {
    }

    public JevConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public JevConfigurationException()
    {
    }
}

/// <summary>Transport failure or invalid envelope returned by the provider.</summary>
public sealed class JevTransportException : Exception
{
    public JevTransportException(string message) : base(message)
    {
    }

    public JevTransportException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public JevTransportException()
    {
    }
}
