using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JevMcp.Providers.Clients;

/// <summary>
/// Common adapter logic: build the POST, redact the credential in errors, and validate
/// the envelope. Differences in URL, envelope, and model name stay in each adapter.
/// </summary>
internal abstract class JevClientBase : IJevClient
{
    protected JevClientBase(HttpClient http, JevProviderOptions options)
    {
        Http = http;
        Options = options;
    }

    public abstract JevProviderKind Provider { get; }

    /// <summary>How this provider is named in error messages.</summary>
    protected abstract string Label { get; }

    protected HttpClient Http { get; }

    protected JevProviderOptions Options { get; }

    public abstract Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary>Model requested on the call, or the configured one.</summary>
    protected string ModelOrDefault(string? model)
    {
        return string.IsNullOrWhiteSpace(model) ? Options.Model : model;
    }

    /// <summary>Authenticated POST, without interpreting the result.</summary>
    protected async Task<(HttpResponseMessage Response, string Content)> PostAsync(
        Uri url,
        string? bearerToken,
        JsonObject body,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return (response, content);
    }

    /// <summary>POST that fails on error status and returns the already-validated envelope.</summary>
    protected async Task<JevWireEnvelope> PostForEnvelopeAsync(
        Uri url,
        string? bearerToken,
        JsonObject body,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken,
        string inputTokensKey = "input_tokens",
        string outputTokensKey = "output_tokens")
    {
        var (response, content) = await PostAsync(url, bearerToken, body, headers, cancellationToken)
            .ConfigureAwait(false);

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw Failure(response, content);
            }

            using var document = ParseOrThrow(content);

            return JevWire.ReadEnvelope(document.RootElement, Label, inputTokensKey, outputTokensKey);
        }
    }

    /// <summary>HTTP error with the credential redacted and the body truncated.</summary>
    protected JevTransportException Failure(HttpResponseMessage response, string content)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = Secrets.Summarize(content, Options.SecretFor(Provider));

        return new JevTransportException($"{Label} {(int)response.StatusCode}: {body}");
    }

    protected JsonDocument ParseOrThrow(string content)
    {
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            throw JevWire.Invalid(Label, "expected a JSON object.");
        }
    }

    protected static JsonObject RequestBody(string model, JsonNode state, JsonObject questions)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new JsonObject
        {
            ["model"] = model,
            ["state"] = state.DeepClone(),
            ["questions"] = questions,
        };
    }
}
