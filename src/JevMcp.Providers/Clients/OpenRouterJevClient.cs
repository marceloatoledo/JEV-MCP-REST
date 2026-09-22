using System.Text.Json.Nodes;

namespace JevMcp.Providers.Clients;

/// <summary>
/// OpenRouter Decisions. There is no redirecting `latest` alias there, so the alias is
/// mapped to the current release; pinning another version is the model variable's job.
/// </summary>
internal sealed class OpenRouterJevClient : JevClientBase
{
    public const string LatestModel = "jev-1.13";

    private const string Title = "jev-mcp";
    private const string Referer = "https://github.com/jkudish/jev-mcp";

    public OpenRouterJevClient(HttpClient http, JevProviderOptions options) : base(http, options)
    {
    }

    public override JevProviderKind Provider => JevProviderKind.OpenRouter;

    protected override string Label => "OpenRouter decisions API";

    /// <summary>Model slug in the OpenRouter namespace.</summary>
    public static string SlugFor(string model)
    {
        var effective = model == JevProviderOptions.DefaultModel ? LatestModel : model;

        return effective.StartsWith("typesafe/", StringComparison.Ordinal) ? effective : $"typesafe/{effective}";
    }

    public override async Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questions);

        var slug = SlugFor(ModelOrDefault(model));

        var envelope = await PostForEnvelopeAsync(
            new Uri(JevProviderOptions.DefaultOpenRouterBaseUrl),
            Options.OpenRouterApiKey,
            RequestBody(slug, state, JevWire.BuildQuestions(questions)),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HTTP-Referer"] = Referer,
                ["X-Title"] = Title,
                ["X-OpenRouter-Title"] = Title,
            },
            cancellationToken).ConfigureAwait(false);

        return new JevAskResult(envelope.Answers, envelope.Usage, Provider, slug);
    }
}
