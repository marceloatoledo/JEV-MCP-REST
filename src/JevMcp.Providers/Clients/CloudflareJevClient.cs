using System.Text.Json;
using System.Text.Json.Nodes;

namespace JevMcp.Providers.Clients;

/// <summary>
/// Cloudflare Workers AI. The same contract is wrapped in `{model, input}` on the way
/// out and in the v4 `{result, success}` envelope on the way back. There is a single
/// alias, with no versioning.
/// </summary>
internal sealed class CloudflareJevClient : JevClientBase
{
    public CloudflareJevClient(HttpClient http, JevProviderOptions options) : base(http, options)
    {
    }

    public override JevProviderKind Provider => JevProviderKind.Cloudflare;

    protected override string Label => "Cloudflare AI run";

    /// <summary>Model slug in the Cloudflare catalog.</summary>
    public static string SlugFor(string model)
    {
        if (model.StartsWith("typesafe/", StringComparison.Ordinal))
        {
            return model;
        }

        return $"typesafe/{(model == JevProviderOptions.DefaultModel ? "jev" : model)}";
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
        var origin = JevProviderOptions.DefaultCloudflareBaseUrl.TrimEnd('/');
        var url = new Uri($"{origin}/accounts/{Options.CloudflareAccountId}/ai/run");

        var body = new JsonObject
        {
            ["model"] = slug,
            ["input"] = new JsonObject
            {
                ["state"] = state.DeepClone(),
                ["questions"] = JevWire.BuildQuestions(questions),
            },
        };

        var (response, content) = await PostAsync(url, Options.CloudflareApiToken, body, null, cancellationToken)
            .ConfigureAwait(false);

        using (response)
        {
            // The v4 envelope can report failure with status 200, so the body is read
            // in both cases and the success check looks at both.
            using var document = TryParse(content);
            var root = document?.RootElement;

            if (!response.IsSuccessStatusCode || ReportsFailure(root))
            {
                throw new JevTransportException(
                    $"{Label} {(int)response.StatusCode}: {ErrorDetail(root, content)}");
            }

            if (root is null)
            {
                throw JevWire.Invalid(Label, "expected a JSON object.");
            }

            var outer = Property(root.Value, "result");
            var runState = outer is null ? null : ReadString(outer.Value, "state");
            if (runState is not null && runState != "Completed")
            {
                throw new JevTransportException($"{Label} state {runState}: {ErrorDetail(root, "[]")}");
            }

            // The envelope nests twice: result.result carries the model output.
            var payload = (outer is null ? null : Property(outer.Value, "result")) ?? outer ?? root;
            var envelope = JevWire.ReadEnvelope(payload.Value, Label);

            return new JevAskResult(envelope.Answers, envelope.Usage, Provider, envelope.Model ?? slug);
        }
    }

    private static bool ReportsFailure(JsonElement? root)
    {
        return root is { } element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False;
    }

    private string ErrorDetail(JsonElement? root, string fallback)
    {
        var detail = fallback;

        if (root is { } element && element.ValueKind == JsonValueKind.Object)
        {
            detail = element.TryGetProperty("errors", out var errors)
                ? errors.GetRawText()
                : element.GetRawText();
        }

        return Secrets.Summarize(detail, Options.SecretFor(Provider));
    }

    private static JsonElement? Property(JsonElement owner, string name)
    {
        return owner.ValueKind == JsonValueKind.Object &&
            owner.TryGetProperty(name, out var value) &&
            value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value
            : null;
    }

    private static string? ReadString(JsonElement owner, string name)
    {
        return owner.ValueKind == JsonValueKind.Object &&
            owner.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static JsonDocument? TryParse(string content)
    {
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
