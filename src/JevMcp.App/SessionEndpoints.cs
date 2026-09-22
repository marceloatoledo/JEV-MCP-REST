using System.Security.Claims;
using JevMcp.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace JevMcp.App;

internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapAccessControlEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/session", (Delegate)LoginAsync).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/api/session/logout", (Delegate)LogoutAsync).ExcludeFromDescription();
        app.MapPost("/api/setup", (Delegate)SetupAsync).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/api/tokens", (Delegate)IssueTokenAsync).ExcludeFromDescription().DisableAntiforgery();
        app.MapPost("/api/culture", (Delegate)SetCultureAsync).AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext http,
        IOptions<AccessControlOptions> access)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        if (!AdminPasswordLogin.Matches(
            access.Value,
            form["username"].ToString(),
            form["password"].ToString()))
        {
            return Results.Redirect("/login?error=1");
        }

        await SignInAdminAsync(http, access.Value.Username!).ConfigureAwait(false);
        return Results.Redirect("/");
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Results.Redirect("/login");
    }

    private static async Task<IResult> SetupAsync(
        HttpContext http,
        IAccessTokenService tokens,
        AccessControlState state,
        OneTimeSecrets secrets)
    {
        if (!state.SetupRequired)
        {
            return Results.Redirect("/");
        }

        if (!Loopback.IsLocal(http))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var issued = await tokens.CreateAsync("Administrador", http.RequestAborted).ConfigureAwait(false);
        secrets.RememberSetup(issued.Secret);
        state.SetupRequired = false;
        await SignInTokenAsync(http, issued.Record).ConfigureAwait(false);
        return Results.Redirect("/setup");
    }

    private static async Task<IResult> IssueTokenAsync(
        HttpContext http,
        IAccessTokenService tokens,
        TokenIssueRequest? body)
    {
        if (!string.Equals(
            http.User.Identity?.AuthenticationType,
            AdminPasswordLogin.AuthenticationType,
            StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        var name = string.IsNullOrWhiteSpace(body?.Name) ? "MCP" : body.Name.Trim();
        var issued = await tokens.CreateAsync(name, http.RequestAborted).ConfigureAwait(false);
        return Results.Json(new TokenIssueResponse(issued.Secret));
    }

    private static async Task<IResult> SetCultureAsync(HttpContext http)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        if (!UiCultures.TryNormalize(form["culture"].ToString(), out var culture))
        {
            return Results.BadRequest();
        }

        http.Response.Cookies.Append(
            UiCultures.CookieName,
            UiCultures.CookieValue(culture),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
            });

        return Results.LocalRedirect(UiCultures.SafeReturn(form["returnUrl"].ToString()));
    }

    private static Task SignInTokenAsync(HttpContext http, McpAccessToken token)
    {
        return SignInPrincipalAsync(http, AccessControlHost.PrincipalFor(token));
    }

    private static Task SignInAdminAsync(HttpContext http, string username)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, username),
                new Claim(ClaimTypes.Name, username),
                new Claim(AccessClaims.TokenName, username),
            ],
            authenticationType: AdminPasswordLogin.AuthenticationType);

        return SignInPrincipalAsync(http, new ClaimsPrincipal(identity));
    }

    private static Task SignInPrincipalAsync(HttpContext http, ClaimsPrincipal principal)
    {
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = DateTimeOffset.UtcNow,
        };

        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }
}

/// <summary>Holds the first-setup token until the UI reads it once. Then it is gone.</summary>
internal sealed class OneTimeSecrets
{
    private string? _setupSecret;
    private readonly object _gate = new();

    public void RememberSetup(string secret)
    {
        lock (_gate)
        {
            _setupSecret = secret;
        }
    }

    public string? ConsumeSetup()
    {
        lock (_gate)
        {
            var secret = _setupSecret;
            _setupSecret = null;
            return secret;
        }
    }
}

internal sealed record TokenIssueRequest(string? Name);

internal sealed record TokenIssueResponse(string Token);
