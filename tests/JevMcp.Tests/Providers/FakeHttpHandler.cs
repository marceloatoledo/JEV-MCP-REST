using System.Net;
using System.Text;

namespace JevMcp.Tests;

/// <summary>Request observed by the fake handler.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri? Url,
    string Body,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Scripted handler: no test in the default suite makes a real network call.
/// The last script entry repeats, so a one-item script serves N attempts.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<(HttpStatusCode Status, string Body)> _script;

    public FakeHttpHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        : this([(status, body)])
    {
    }

    public FakeHttpHandler(IEnumerable<(HttpStatusCode Status, string Body)> script)
    {
        _script = [.. script];
    }

    public List<RecordedRequest> Requests { get; } = [];

    public RecordedRequest LastRequest => Requests[^1];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, body, headers));

        var (status, responseBody) = _script[Math.Min(Requests.Count, _script.Count) - 1];

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}
