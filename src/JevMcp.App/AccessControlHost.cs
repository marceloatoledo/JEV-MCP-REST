using System.Security.Claims;
using JevMcp.Data;
using Microsoft.Extensions.Options;

namespace JevMcp.App;

internal static class AccessControlHost
{
    public static async Task InitializeAccessControlAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var tokens = app.Services.GetRequiredService<IAccessTokenService>();
        var settings = app.Services.GetRequiredService<IOperationalSettings>();
        var options = app.Services.GetRequiredService<IOptions<AccessControlOptions>>().Value;
        var audit = app.Services.GetRequiredService<IOptions<CallAuditOptions>>().Value;
        var state = app.Services.GetRequiredService<AccessControlState>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("JevMcp.AccessControl");

        await settings.EnsureDefaultsAsync(
            options.AllowAnonymousMcp,
            audit.CapturePayloads,
            audit.RetentionDays).ConfigureAwait(false);

        var hasActive = await tokens.HasActiveAsync().ConfigureAwait(false);
        var anonymous = settings.AllowAnonymousMcp;
        var urls = app.Configuration["urls"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? app.Configuration["applicationUrl"];

        if (Loopback.IsExposedBinding(urls) && !hasActive && !AllowsNoActiveTokens(anonymous, options))
        {
            throw new InvalidOperationException(
                "Refusing to listen on a public address without an access token or admin login. " +
                $"Set {AccessControlOptions.UsernameEnvironmentKey} and {AccessControlOptions.PasswordEnvironmentKey} " +
                $"(or {AccessControlOptions.LegacyUsernameVariable} and {AccessControlOptions.LegacyPasswordVariable}), " +
                "enable ACCESSCONTROL:ALLOWANONYMOUSMCP, or bind to loopback for first-time setup.");
        }

        if (anonymous)
        {
            logger.LogWarning("Anonymous access to /mcp and /api/jev is enabled. Do not use this in production.");
        }

        state.SetupRequired = !hasActive && !AllowsNoActiveTokens(anonymous, options);
    }

    internal static bool AllowsNoActiveTokens(bool allowAnonymousMcp, AccessControlOptions options) =>
        allowAnonymousMcp || options.HasAdminPassword;

    public static ClaimsPrincipal PrincipalFor(McpAccessToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, token.Id.ToString()),
                new Claim(ClaimTypes.Name, token.Name),
                new Claim(AccessClaims.TokenId, token.Id.ToString()),
                new Claim(AccessClaims.TokenName, token.Name),
                new Claim(AccessClaims.TokenPrefix, token.Prefix),
            ],
            authenticationType: "McpAccessToken");

        return new ClaimsPrincipal(identity);
    }
}
