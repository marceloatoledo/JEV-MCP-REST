using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JevMcp.Tools;

namespace JevMcp.Tests;

public sealed class RestApiTests : IClassFixture<AccessAppFactory>
{
    private static readonly string[] ToolNames =
    [
        JevTools.Classify, JevTools.Compare, JevTools.Decide, JevTools.Extract, JevTools.Find,
        JevTools.Gate, JevTools.Rerank, JevTools.Review, JevTools.Screen, JevTools.Verify,
    ];

    private readonly AccessAppFactory _factory;

    public RestApiTests(AccessAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RestWithoutATokenReturns401AndWwwAuthenticate()
    {
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(Json("POST", "/api/jev/screen", """{"text":"hello"}""", token: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestCatalogListsTheTenTools()
    {
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(Json("GET", "/api/jev/tools", body: null, _factory.AccessToken));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var names = document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("tool").GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ToolNames, names);
        Assert.Contains("/api/jev/verify", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestRejectsInvalidInputWith400AndTheFieldName()
    {
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(
            Json("POST", "/api/jev/verify", """{"claims":[],"evidence":"the sky is blue"}""", _factory.AccessToken));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("claims", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestExtractWithoutAMatchDoesNotNeedAProvider()
    {
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(Json(
            "POST",
            "/api/jev/extract",
            """{"document":"nothing here","fields":[{"id":"iban","pattern":"[A-Z]{2}[0-9]{20}","description":"The IBAN"}]}""",
            _factory.AccessToken));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(JevTools.Extract, document.RootElement.GetProperty("tool").GetString());
        Assert.Equal("not_found", document.RootElement.GetProperty("results")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task RestScreenRejectsEmptyText()
    {
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(
            Json("POST", "/api/jev/screen", """{"text":""}""", _factory.AccessToken));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("text", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiDocumentListsTheRestToolsAndHealth()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/jev/verify", payload, StringComparison.Ordinal);
        Assert.Contains("/api/jev/gate", payload, StringComparison.Ordinal);
        Assert.Contains("/health", payload, StringComparison.Ordinal);
        Assert.Contains("Bearer", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.AccessToken, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/session", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScalarUiIsReachableWithoutAToken()
    {
        using var client = _factory.CreateClient();
        using var first = await client.GetAsync("/scalar");
        using var response = first.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found
            or HttpStatusCode.RedirectKeepVerb or HttpStatusCode.PermanentRedirect
            ? await client.GetAsync(first.Headers.Location ?? new Uri("/scalar/v1", UriKind.Relative))
            : first;

        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("scalar", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.AccessToken, html, StringComparison.Ordinal);
    }

    private static HttpRequestMessage Json(string method, string path, string? body, string? token)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }
}
