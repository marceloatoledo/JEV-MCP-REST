using System.Text.Json.Nodes;

namespace JevMcp.Providers.Clients;

/// <summary>
/// Direct TypeSafe, the recommended path: a proxy only adds hops. The route is fixed;
/// `TYPESAFE_BASE_URL` changes only the origin, as in the official SDK.
/// </summary>
internal sealed class TypeSafeJevClient : JevClientBase
{
    public const string Route = "/v1/systemone";

    public TypeSafeJevClient(HttpClient http, JevProviderOptions options) : base(http, options)
    {
    }

    public override JevProviderKind Provider => JevProviderKind.TypeSafe;

    protected override string Label => "TypeSafe System One";

    public override async Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questions);

        var requested = ModelOrDefault(model);
        var origin = (Options.TypeSafeBaseUrl ?? JevProviderOptions.DefaultTypeSafeBaseUrl).TrimEnd('/');
        var url = new Uri(origin + Route);

        var envelope = await PostForEnvelopeAsync(
            url,
            Options.TypeSafeApiKey,
            RequestBody(requested, state, JevWire.BuildQuestions(questions)),
            headers: null,
            cancellationToken).ConfigureAwait(false);

        // The API returns the resolved alias version; that is what counts for audit.
        return new JevAskResult(envelope.Answers, envelope.Usage, Provider, envelope.Model ?? requested);
    }
}
