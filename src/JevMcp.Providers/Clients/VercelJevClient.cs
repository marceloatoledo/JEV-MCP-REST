using System.Text.Json;
using System.Text.Json.Nodes;

namespace JevMcp.Providers.Clients;

/// <summary>
/// Vercel AI Gateway. The gateway exposes Jev through the AI SDK evaluation model: the
/// noul primitive is named `boolean`, the model goes in a header instead of the body,
/// and Choice and Score confidence lives in provider metadata.
/// </summary>
internal sealed class VercelJevClient : JevClientBase
{
    public const string Route = "/evaluation-model";
    public const string DefaultSlug = "typesafe-ai/jev";

    private const string SpecificationVersionHeader = "ai-evaluation-model-specification-version";
    private const string ModelHeader = "ai-model-id";

    public VercelJevClient(HttpClient http, JevProviderOptions options) : base(http, options)
    {
    }

    public override JevProviderKind Provider => JevProviderKind.Vercel;

    protected override string Label => "Vercel AI Gateway";

    /// <summary>The gateway only knows its own namespace; any other alias falls back to the default.</summary>
    public static string SlugFor(string model)
    {
        return model.StartsWith("typesafe-ai/", StringComparison.Ordinal) ? model : DefaultSlug;
    }

    public override async Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);

        var slug = SlugFor(ModelOrDefault(model));
        var origin = (Options.AiGatewayBaseUrl ?? JevProviderOptions.DefaultAiGatewayBaseUrl).TrimEnd('/');

        var body = new JsonObject
        {
            ["state"] = state.DeepClone(),
            ["questions"] = JevWire.BuildQuestions(questions, noulAsBoolean: true),
        };

        var (response, content) = await PostAsync(
            new Uri(origin + Route),
            Options.AiGatewayApiKey,
            body,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SpecificationVersionHeader] = "4",
                [ModelHeader] = slug,
            },
            cancellationToken).ConfigureAwait(false);

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw Failure(response, content);
            }

            using var document = ParseOrThrow(content);
            var confidence = ConfidenceFrom(document.RootElement);

            var envelope = JevWire.ReadEnvelope(
                document.RootElement,
                Label,
                inputTokensKey: "inputTokens",
                outputTokensKey: "outputTokens",
                answerParser: answers => JevWire.ParseVercelAnswers(answers, confidence));

            return new JevAskResult(envelope.Answers, envelope.Usage, Provider, slug);
        }
    }

    private static IReadOnlyDictionary<string, double> ConfidenceFrom(JsonElement root)
    {
        var confidence = new Dictionary<string, double>(StringComparer.Ordinal);

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("providerMetadata", out var metadata) ||
            metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty("typesafe", out var typesafe) ||
            typesafe.ValueKind != JsonValueKind.Object ||
            !typesafe.TryGetProperty("confidence", out var byId) ||
            byId.ValueKind != JsonValueKind.Object)
        {
            return confidence;
        }

        foreach (var property in byId.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var value))
            {
                confidence[property.Name] = value;
            }
        }

        return confidence;
    }
}
