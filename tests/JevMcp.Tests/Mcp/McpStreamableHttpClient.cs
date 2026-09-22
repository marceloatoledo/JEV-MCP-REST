using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JevMcp.Tests.Mcp;

/// <summary>
/// Minimal Streamable HTTP MCP client matching Cursor: initialize, GET SSE, tools/list, tools/call.
/// After the SSE stream is open, POSTs may return 202 with JSON-RPC on the event stream.
/// </summary>
internal sealed class McpStreamableHttpClient : IAsyncDisposable
{
    private const string SessionHeader = "MCP-Session-Id";

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingRpc = new();
    private string? _sessionId;
    private int _nextId = 2;
    private HttpResponseMessage? _sseResponse;
    private CancellationTokenSource? _sseCts;
    private Task? _ssePump;

    public McpStreamableHttpClient(HttpClient http, string bearerToken)
    {
        _http = http;
        _token = bearerToken;
    }

    public string? SessionId => _sessionId;

    public async Task ConnectCursorStyleAsync(CancellationToken cancellationToken = default)
    {
        var (initialize, headers) = await PostAsync(
            """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "initialize",
              "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": { "name": "JEV-MCP-tests", "version": "1.0" }
              }
            }
            """,
            cancellationToken).ConfigureAwait(false);

        if (initialize.RootElement.TryGetProperty("error", out var initError))
        {
            throw new InvalidOperationException(initError.GetRawText());
        }

        _sessionId = ReadSessionId(headers);
        if (string.IsNullOrWhiteSpace(_sessionId))
        {
            throw new InvalidOperationException("initialize did not return MCP-Session-Id.");
        }

        await OpenEventStreamAsync(cancellationToken).ConfigureAwait(false);

        _ = await PostAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListToolNamesAsync(CancellationToken cancellationToken = default)
    {
        using var document = await PostRpcAsync(
            $$"""{"jsonrpc":"2.0","id":{{_nextId++}},"method":"tools/list"}""",
            cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(error.GetRawText());
        }

        return root.GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<JsonDocument> CallToolAsync(
        string name,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        var body = $$"""
            {
              "jsonrpc": "2.0",
              "id": {{_nextId++}},
              "method": "tools/call",
              "params": {
                "name": {{JsonSerializer.Serialize(name)}},
                "arguments": {{argumentsJson}}
              }
            }
            """;

        return await PostRpcAsync(body, cancellationToken).ConfigureAwait(false);
    }

    public async Task OpenEventStreamAsync(CancellationToken cancellationToken = default)
    {
        if (_ssePump is not null)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.TryAddWithoutValidation(SessionHeader, _sessionId);

        _sseResponse = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (_sseResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"GET /mcp returned {(int)_sseResponse.StatusCode}.");
        }

        _sseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stream = await _sseResponse.Content.ReadAsStreamAsync(_sseCts.Token).ConfigureAwait(false);
        _ssePump = PumpSseAsync(stream, _sseCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _sseCts?.Cancel();
        if (_ssePump is not null)
        {
            try
            {
                await _ssePump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _sseResponse?.Dispose();
        _sseCts?.Dispose();

        foreach (var pending in _pendingRpc.Values)
        {
            pending.TrySetCanceled();
        }

        _pendingRpc.Clear();
    }

    private async Task PumpSseAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data: ".Length..];
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            if (!TryGetResponseId(payload, out var id))
            {
                continue;
            }

            if (_pendingRpc.TryRemove(id, out var tcs))
            {
                tcs.TrySetResult(payload);
            }
        }
    }

    private async Task<JsonDocument> PostRpcAsync(string json, CancellationToken cancellationToken)
    {
        var (document, _) = await PostAsync(json, cancellationToken).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(error.GetRawText());
        }

        return document;
    }

    private async Task<(JsonDocument Document, HttpResponseHeaders Headers)> PostAsync(
        string json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (!string.IsNullOrEmpty(_sessionId))
        {
            request.Headers.TryAddWithoutValidation(SessionHeader, _sessionId);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var status = response.StatusCode;
        if (status is not HttpStatusCode.OK and not HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException($"POST /mcp returned {(int)status}.");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return (JsonDocument.Parse(ExtractJsonPayload(text)), response.Headers);
        }

        if (status == HttpStatusCode.Accepted && TryGetRequestId(json, out var requestId))
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRpc.TryAdd(requestId, tcs))
            {
                throw new InvalidOperationException($"Duplicate JSON-RPC id {requestId}.");
            }

            try
            {
                var payload = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                    .ConfigureAwait(false);
                return (JsonDocument.Parse(payload), response.Headers);
            }
            catch (TimeoutException ex)
            {
                _pendingRpc.TryRemove(requestId, out _);
                throw new InvalidOperationException($"Timed out waiting for SSE response to id {requestId}.", ex);
            }
        }

        return (JsonDocument.Parse("{}"), response.Headers);
    }

    private static bool TryGetRequestId(string json, out int id)
    {
        id = 0;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out id))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetResponseId(string json, out int id)
    {
        id = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("id", out var idElement))
            {
                return false;
            }

            return idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out id);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ReadSessionId(HttpResponseHeaders headers)
    {
        if (headers.TryGetValues(SessionHeader, out var values))
        {
            return values.First();
        }

        return headers
            .FirstOrDefault(header => string.Equals(header.Key, SessionHeader, StringComparison.OrdinalIgnoreCase))
            .Value?.FirstOrDefault() ?? string.Empty;
    }

    private static string ExtractJsonPayload(string content)
    {
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return line["data: ".Length..];
            }
        }

        return content;
    }
}
