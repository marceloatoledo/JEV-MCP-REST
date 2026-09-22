using JevMcp.Data;

namespace JevMcp.App;

/// <summary>
/// Validates Bearer on <c>/mcp</c> and <c>/api/jev</c>. The rest of the app uses a
/// cookie; these paths are stateless and cannot depend on a session.
/// </summary>
internal sealed class McpBearerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IAccessTokenService tokens,
        IOperationalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!MachineHttpPaths.RequiresAccessToken(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (settings.AllowAnonymousMcp)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var presented = ReadBearer(context.Request.Headers.Authorization);
        var match = await tokens.AuthenticateAsync(presented, context.RequestAborted).ConfigureAwait(false);
        if (match is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        context.User = AccessControlHost.PrincipalFor(match);
        await next(context).ConfigureAwait(false);
    }

    private static string? ReadBearer(string? header)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = header[prefix.Length..].Trim();
        return value.Length == 0 ? null : value;
    }
}

/// <summary>
/// First-time setup is loopback-only. Login stays reachable so the operator is
/// not sent to setup when opening the app; setup remains available locally.
/// </summary>
internal sealed class SetupGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AccessControlState state)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!state.SetupRequired)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var path = context.Request.Path;
        if (MachineHttpPaths.BypassesSetupGate(path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!Loopback.IsLocal(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (AdminPublicPaths.AllowsAnonymous(path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        context.Response.Redirect("/login");
    }
}

/// <summary>
/// Cookie auth does not HTTP-challenge Blazor Interactive Server pages. Send
/// anonymous document requests to login instead of rendering the admin shell.
/// </summary>
internal sealed class AdminLoginRedirectMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated == true ||
            !HttpMethods.IsGet(context.Request.Method) ||
            AdminPublicPaths.AllowsAnonymous(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        context.Response.Redirect("/login");
    }
}

internal static class MachineHttpPaths
{
    public static bool RequiresAccessToken(PathString path) =>
        path.StartsWithSegments("/mcp") || path.StartsWithSegments("/api/jev");

    public static bool BypassesSetupGate(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/mcp") ||
        path.StartsWithSegments("/api/jev") ||
        path.StartsWithSegments("/openapi") ||
        path.StartsWithSegments("/scalar");

    public static bool IsStatusRaw(PathString path) =>
        BypassesSetupGate(path) || path.StartsWithSegments("/api");
}

internal static class AdminPublicPaths
{
    public static bool AllowsAnonymous(PathString path)
    {
        return path.StartsWithSegments("/login") ||
            path.StartsWithSegments("/setup") ||
            path.StartsWithSegments("/api") ||
            path.StartsWithSegments("/not-found") ||
            path.StartsWithSegments("/Error") ||
            MachineHttpPaths.BypassesSetupGate(path) ||
            IsStaticAsset(path);
    }

    private static bool IsStaticAsset(PathString path)
    {
        var value = path.Value ?? "";
        return value.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("/favicon.png", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
