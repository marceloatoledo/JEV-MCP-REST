using System.Text.Json;
using JevMcp.App;
using JevMcp.App.Components;
using JevMcp.Data;
using JevMcp.Providers;
using JevMcp.Tools;
using JevMcp.Tools.Mcp;
using Microsoft.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture(UiCultures.Default)
        .AddSupportedCultures(UiCultures.Supported)
        .AddSupportedUICultures(UiCultures.Supported);
    options.RequestCultureProviders = [new UiCultureProvider()];
    options.ApplyCurrentCultureToResponseHeaders = true;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.WriteIndented = true;
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<JevOpenApiTransformer>();
    options.AddOperationTransformer<JevRestSecurityTransformer>();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<AccessControlState>();
builder.Services.AddScoped<ThemeState>();
builder.Services.AddSingleton<OneTimeSecrets>();
builder.Services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
builder.Services.AddSingleton<IProjectReadmeSource, ProjectReadmeSource>();
builder.Services.AddSingleton(provider => new CredentialOverview(
    provider.GetRequiredService<JevProviderOptions>(),
    provider.GetRequiredService<JevProviderResolution>(),
    provider.GetRequiredService<IOptions<AccessControlOptions>>().Value));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "jevmcp.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            if (string.Equals(
                context.Principal?.Identity?.AuthenticationType,
                AdminPasswordLogin.AuthenticationType,
                StringComparison.Ordinal))
            {
                var access = context.HttpContext.RequestServices
                    .GetRequiredService<IOptions<AccessControlOptions>>()
                    .Value;
                var name = context.Principal!.Identity?.Name;
                if (!access.HasAdminPassword ||
                    !string.Equals(access.Username, name, StringComparison.Ordinal))
                {
                    context.RejectPrincipal();
                }

                return;
            }

            var idValue = context.Principal?.FindFirst(AccessClaims.TokenId)?.Value;
            if (!long.TryParse(idValue, out var id))
            {
                context.RejectPrincipal();
                return;
            }

            var tokens = context.HttpContext.RequestServices.GetRequiredService<IAccessTokenService>();
            if (!await tokens.IsActiveAsync(id, context.HttpContext.RequestAborted).ConfigureAwait(false))
            {
                context.RejectPrincipal();
            }
        };
    });
builder.Services.AddAuthorization();

// Provider resolution happens here, at startup. Without a credential the app still
// starts and reports why, instead of failing on the first tool an agent calls.
builder.Services.AddJevMcpTools(JevProviderConfiguration.Load(builder.Configuration));
builder.Services.AddJevMcpData(builder.Configuration);
builder.Services.AddSingleton<UiDisplayTime>();

// Hybrid transport: clients that still use initialize + GET SSE (e.g. Cursor) get a
// stateful session; 2026-07-28+ clients stay per-request. Pure Stateless disables GET
// on /mcp and breaks those hosts ("Failed to open SSE stream: Not Found").
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients)
    .WithRequestFilters(filters => filters.AddCallAudit())
    .WithTools<JevVerifyTool>()
    .WithTools<JevScreenTool>()
    .WithTools<JevFindTool>()
    .WithTools<JevClassifyTool>()
    .WithTools<JevDecideTool>()
    .WithTools<JevRerankTool>()
    .WithTools<JevCompareTool>()
    .WithTools<JevExtractTool>()
    .WithTools<JevReviewTool>()
    .WithTools<JevGateTool>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// 401/403 from /mcp and APIs must not become the HTML 404 page: the MCP client
// needs the original status and WWW-Authenticate.
app.UseRequestLocalization();
// wwwroot (favicon.png, etc.) before setup/login gates; ends the pipeline when the file exists.
app.UseStaticFiles();

app.UseWhen(
    context => !MachineHttpPaths.IsStatusRaw(context.Request.Path),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseMiddleware<SetupGateMiddleware>();
app.UseAuthentication();
app.UseMiddleware<AdminLoginRedirectMiddleware>();
app.UseMiddleware<McpBearerMiddleware>();
app.UseAuthorization();

// Razor components carry antiforgery metadata and require the middleware; the
// MCP endpoint has no such metadata and passes through without validation.
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAccessControlEndpoints();
app.MapJevRest();
app.MapMcp("/mcp");
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference(options => options.WithTitle("JEV-MCP")).AllowAnonymous();

app.MapGet("/health", (JevProviderResolution resolution, JevProviderOptions jev) => Results.Ok(new HealthStatusResponse(
    resolution.IsConfigured ? "ok" : "degraded",
    resolution.Provider?.ToString().ToLowerInvariant(),
    jev.Model,
    resolution.Error)))
    .WithTags("Health")
    .WithSummary("Liveness and provider status")
    .AllowAnonymous()
    .Produces<HealthStatusResponse>();

app.InitializeCallAudit();
await app.InitializeAccessControlAsync();
await app.BackfillHistoricalCostsAsync();
app.Run();

public partial class Program;
