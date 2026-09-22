using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JevMcp.App;
using JevMcp.Data;
using JevMcp.Tests.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JevMcp.Tests;

public sealed class AccessControlHttpTests : IClassFixture<AccessAppFactory>
{
    private readonly AccessAppFactory _factory;

    public AccessControlHttpTests(AccessAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task McpWithoutATokenReturns401AndDoesNotCallTheProvider()
    {
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(McpRequest(null, ToolsListBody));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            "Bearer",
            response.Headers.WwwAuthenticate.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task McpWithAValidTokenListsTheTools()
    {
        using var client = _factory.CreateClient();
        await using var mcp = new McpStreamableHttpClient(client, _factory.AccessToken);
        await mcp.ConnectCursorStyleAsync();

        var tools = await mcp.ListToolNamesAsync();
        Assert.Contains("jev_verify", tools);
        Assert.Contains("jev_gate", tools);
    }

    [Fact]
    public async Task McpWithARevokedTokenReturns401()
    {
        var tokens = _factory.Services.GetRequiredService<IAccessTokenService>();
        var issued = await tokens.CreateAsync("Revogar");
        await tokens.RevokeAsync(issued.Record.Id);

        using var client = _factory.CreateClient();
        using var revoked = await client.SendAsync(McpRequest(issued.Secret, ToolsListBody));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        await using var mcp = new McpStreamableHttpClient(client, _factory.AccessToken);
        await mcp.ConnectCursorStyleAsync();
        var tools = await mcp.ListToolNamesAsync();
        Assert.NotEmpty(tools);
    }

    [Fact]
    public async Task McpWithAnUnknownPrefixReturns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(McpRequest("other_not_this_server_token", ToolsListBody));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthStaysOpenAndDoesNotRevealTheToken()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_factory.AccessToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.AdminPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain("authorization", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginUsesPortugueseWhenAcceptLanguageIsUnsupported()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/login");
        request.Headers.AcceptLanguage.ParseAdd("de");
        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Entrar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sign in", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginHonorsEnglishAcceptLanguage()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/login");
        request.Headers.AcceptLanguage.ParseAdd("en-US");
        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign in", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Entrar", html, StringComparison.Ordinal);
        Assert.Contains("name=\"username\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"password\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"token\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginCultureCookieBeatsAcceptLanguage()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/login");
        request.Headers.AcceptLanguage.ParseAdd("es");
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{UiCultures.CookieName}={UiCultures.CookieValue("en")}");
        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign in", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Use un token de acceso", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpCallAuditStoresTheOriginToken()
    {
        using var client = _factory.CreateClient();
        await using var mcp = new McpStreamableHttpClient(client, _factory.AccessToken);
        await mcp.ConnectCursorStyleAsync();
        using var response = await mcp.CallToolAsync(
            "jev_screen",
            """{"text":"probe"}""");
        Assert.False(response.RootElement.TryGetProperty("error", out _));

        var writer = _factory.Services.GetRequiredService<CallLogWriter>();
        using var idle = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await writer.WaitForIdleAsync(idle.Token);

        var db = _factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = db.CreateDbContext();
        var log = Assert.Single(await context.CallLogs.AsNoTracking()
            .Where(entry => entry.Tool == "jev_screen")
            .ToListAsync());
        Assert.Equal("Tests", log.TokenName);
        Assert.False(string.IsNullOrEmpty(log.TokenPrefix));
        var tokenRow = await context.AccessTokens.AsNoTracking()
            .SingleAsync(entry => entry.Name == "Tests");
        Assert.Equal(tokenRow.Id, log.AccessTokenId);
        Assert.DoesNotContain(_factory.AccessToken, log.TokenPrefix, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.AccessToken, log.Error ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.AdminPassword, log.Error ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootRedirectsAnonymousUsersToLogin()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AuditRedirectsAnonymousUsersToLogin()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/audit");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LoginWithConfiguredPasswordSetsASessionCookie()
    {
        using var client = _factory.CreateClient();
        using var page = await client.GetAsync("/login");
        var html = await page.Content.ReadAsStringAsync();

        using var response = await client.PostAsync(
            "/api/session",
            Form(html, new Dictionary<string, string>
            {
                ["username"] = _factory.AdminUsername,
                ["password"] = _factory.AdminPassword,
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers, header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoginWithWrongPasswordDoesNotCreateASession()
    {
        using var client = _factory.CreateClient();
        using var page = await client.GetAsync("/login");
        var html = await page.Content.ReadAsStringAsync();

        using var response = await client.PostAsync(
            "/api/session",
            Form(html, new Dictionary<string, string>
            {
                ["username"] = _factory.AdminUsername,
                ["password"] = "wrong-password",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=1", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootServesTheDashboardAfterPasswordLogin()
    {
        using var client = _factory.CreateClient();
        using var page = await client.GetAsync("/login");
        var html = await page.Content.ReadAsStringAsync();

        using var login = await client.PostAsync(
            "/api/session",
            Form(html, new Dictionary<string, string>
            {
                ["username"] = _factory.AdminUsername,
                ["password"] = _factory.AdminPassword,
            }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var home = await client.GetAsync("/");
        var body = await home.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Painel", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminSessionCanIssueAnMcpToken()
    {
        using var client = _factory.CreateClient();
        using var page = await client.GetAsync("/login");
        var html = await page.Content.ReadAsStringAsync();
        using var login = await client.PostAsync(
            "/api/session",
            Form(html, new Dictionary<string, string>
            {
                ["username"] = _factory.AdminUsername,
                ["password"] = _factory.AdminPassword,
            }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var issued = await client.PostAsync(
            "/api/tokens",
            new StringContent("""{"name":"smoke"}""", Encoding.UTF8, "application/json"));
        var payload = await issued.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var document = JsonDocument.Parse(payload);
        var token = document.RootElement.GetProperty("token").GetString();
        Assert.StartsWith("jevmcp_", token, StringComparison.Ordinal);

        await using var mcpClient = new McpStreamableHttpClient(client, token!);
        await mcpClient.ConnectCursorStyleAsync();
        var tools = await mcpClient.ListToolNamesAsync();
        Assert.NotEmpty(tools);
    }

    private static FormUrlEncodedContent Form(string html, Dictionary<string, string> fields)
    {
        var antiForgery = AntiForgery(html);
        if (antiForgery is not null)
        {
            fields["__RequestVerificationToken"] = antiForgery;
        }

        return new FormUrlEncodedContent(fields);
    }

    private static string? AntiForgery(string html)
    {
        const string namedFirst = "name=\"__RequestVerificationToken\"";
        const string valueFirst = "value=\"";
        var named = html.IndexOf(namedFirst, StringComparison.Ordinal);
        if (named >= 0)
        {
            var valueAt = html.IndexOf("value=\"", named, StringComparison.Ordinal);
            if (valueAt >= 0)
            {
                var start = valueAt + valueFirst.Length;
                var end = html.IndexOf('"', start);
                if (end > start)
                {
                    return html[start..end];
                }
            }
        }

        var value = html.IndexOf("value=\"", StringComparison.Ordinal);
        while (value >= 0)
        {
            var start = value + valueFirst.Length;
            var end = html.IndexOf('"', start);
            if (end > start)
            {
                var nearby = html[Math.Max(0, value - 80)..Math.Min(html.Length, end + 80)];
                if (nearby.Contains("__RequestVerificationToken", StringComparison.Ordinal))
                {
                    return html[start..end];
                }
            }

            value = html.IndexOf("value=\"", start, StringComparison.Ordinal);
        }

        return null;
    }

    private const string ToolsListBody = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

    private static HttpRequestMessage McpRequest(string? token, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }
}

public class AccessAppFactory : WebApplicationFactory<Program>
{
    private readonly string _directory = Directory.CreateTempSubdirectory("JevMcp-access-").FullName;
    private string? _accessToken;

    public AccessAppFactory()
    {
        ClientOptions.AllowAutoRedirect = false;
    }

    public string AccessToken
    {
        get
        {
            if (_accessToken is not null)
            {
                return _accessToken;
            }

            _ = Server;
            _accessToken = Services.GetRequiredService<IAccessTokenService>()
                .CreateAsync("Tests")
                .GetAwaiter()
                .GetResult()
                .Secret;
            return _accessToken;
        }
    }

    public string AdminUsername { get; } = "operator";

    public string AdminPassword { get; } = "correct-horse-battery-staple";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ConfigureAccessWebHost(builder, Path.Combine(_directory, "audit.db"));
    }

    protected virtual void ConfigureAccessWebHost(IWebHostBuilder builder, string db)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("urls", "http://127.0.0.1");
        builder.UseSetting("CALLAUDIT:DATABASEPATH", db);
        builder.UseSetting("ACCESSCONTROL:USERNAME", AdminUsername);
        builder.UseSetting("ACCESSCONTROL:PASSWORD", AdminPassword);
        builder.UseSetting("ACCESSCONTROL:ALLOWANONYMOUSMCP", "false");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CALLAUDIT:DATABASEPATH"] = db,
                ["ACCESSCONTROL:USERNAME"] = AdminUsername,
                ["ACCESSCONTROL:PASSWORD"] = AdminPassword,
                ["ACCESSCONTROL:ALLOWANONYMOUSMCP"] = "false",
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class SetupRequiredHttpTests : IClassFixture<SetupRequiredAppFactory>
{
    private readonly SetupRequiredAppFactory _factory;

    public SetupRequiredHttpTests(SetupRequiredAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RootRedirectsToLoginInsteadOfSetup()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LoginIsReachableBeforeTheFirstToken()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"username\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"password\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetupStaysReachableOnLoopback()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class SetupRequiredAppFactory : WebApplicationFactory<Program>
{
    private readonly string _directory = Directory.CreateTempSubdirectory("JevMcp-setup-").FullName;

    public SetupRequiredAppFactory()
    {
        ClientOptions.AllowAutoRedirect = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var db = Path.Combine(_directory, "audit.db");
        builder.UseEnvironment("Development");
        builder.UseSetting("urls", "http://127.0.0.1");
        builder.UseSetting("CALLAUDIT:DATABASEPATH", db);
        builder.UseSetting("ACCESSCONTROL:USERNAME", "");
        builder.UseSetting("ACCESSCONTROL:PASSWORD", "");
        builder.UseSetting("ACCESSCONTROL:ALLOWANONYMOUSMCP", "false");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CALLAUDIT:DATABASEPATH"] = db,
                ["ACCESSCONTROL:USERNAME"] = "",
                ["ACCESSCONTROL:PASSWORD"] = "",
                ["ACCESSCONTROL:ALLOWANONYMOUSMCP"] = "false",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<AccessControlOptions>(options =>
            {
                options.Username = null;
                options.Password = null;
                options.AllowAnonymousMcp = false;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
