using System.Net.Http.Headers;

namespace JevMcp.Providers;

/// <summary>
/// Retry with exponential backoff and jitter on 429 and 529, the two codes that
/// System One documentation says to retry. A 401 or 422 is not retried: repeating
/// a wrong credential or an invalid body only burns quota.
/// </summary>
internal sealed class JevRetryHandler : DelegatingHandler
{
    private readonly JevRetryOptions _options;

    public JevRetryHandler(JevRetryOptions options)
    {
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attempts = Math.Max(1, _options.MaxAttempts);

        // The body is read once: resending the same HttpRequestMessage is not supported,
        // so each attempt sends a copy.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var contentType = request.Content?.Headers.ContentType;

        for (var attempt = 1; ; attempt++)
        {
            var attemptRequest = Clone(request, body, contentType);
            var response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);

            if (attempt >= attempts || !IsTransient(response))
            {
                return response;
            }

            response.Dispose();
            attemptRequest.Dispose();

            await Task.Delay(DelayFor(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpResponseMessage response)
    {
        return (int)response.StatusCode is 429 or 529;
    }

    private static HttpRequestMessage Clone(
        HttpRequestMessage request,
        byte[]? body,
        MediaTypeHeaderValue? contentType)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            clone.Content.Headers.ContentType = contentType;
        }

        return clone;
    }

    private TimeSpan DelayFor(int attempt)
    {
        var exponential = _options.BaseDelay * Math.Pow(2, attempt - 1);
        var capped = exponential < _options.MaxDelay ? exponential : _options.MaxDelay;
        var jitter = capped * Random.Shared.NextDouble() * 0.5;
        var total = capped + jitter;

        return total < _options.MaxDelay ? total : _options.MaxDelay;
    }
}
