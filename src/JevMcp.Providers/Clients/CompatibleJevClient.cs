using System.Text.Json.Nodes;

namespace JevMcp.Providers.Clients;

/// <summary>
/// Caller-supplied System One-compatible endpoint. The URL is used verbatim, without
/// normalizing a trailing slash or path: it is already the full POST URL.
/// </summary>
internal sealed class CompatibleJevClient : JevClientBase
{
    public CompatibleJevClient(HttpClient http, JevProviderOptions options) : base(http, options)
    {
    }

    public override JevProviderKind Provider => JevProviderKind.Compatible;

    protected override string Label => "Jev-compatible endpoint";

    public override async Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questions);

        if (string.IsNullOrEmpty(Options.CompatibleBaseUrl))
        {
            throw new JevConfigurationException("JEV_API_BASE_URL is not set.");
        }

        var requested = ModelOrDefault(model);

        var envelope = await PostForEnvelopeAsync(
            new Uri(Options.CompatibleBaseUrl),
            Options.CompatibleApiKey,
            RequestBody(requested, state, JevWire.BuildQuestions(questions)),
            headers: null,
            cancellationToken).ConfigureAwait(false);

        return new JevAskResult(envelope.Answers, envelope.Usage, Provider, envelope.Model ?? requested);
    }
}
