using System.Text.Json.Nodes;
using JevMcp.Providers.Clients;

namespace JevMcp.Providers;

/// <summary>Construction of an adapter for an already-resolved provider.</summary>
public static class JevClients
{
    public static IJevClient Create(JevProviderKind provider, HttpClient http, JevProviderOptions options)
    {
        return provider switch
        {
            JevProviderKind.TypeSafe => new TypeSafeJevClient(http, options),
            JevProviderKind.OpenRouter => new OpenRouterJevClient(http, options),
            JevProviderKind.Cloudflare => new CloudflareJevClient(http, options),
            JevProviderKind.Vercel => new VercelJevClient(http, options),
            JevProviderKind.Compatible => new CompatibleJevClient(http, options),
            _ => throw new JevConfigurationException($"Unknown Jev provider {provider}."),
        };
    }
}

/// <summary>
/// Client used when no provider is configured. The application starts, `/health` and
/// admin show the reason, and a tool call fails as a configuration error.
/// </summary>
internal sealed class UnconfiguredJevClient : IJevClient
{
    private readonly JevProviderResolution _resolution;

    public UnconfiguredJevClient(JevProviderResolution resolution)
    {
        _resolution = resolution;
    }

    public Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        _resolution.Require();

        throw new JevConfigurationException("No Jev provider is configured.");
    }
}
