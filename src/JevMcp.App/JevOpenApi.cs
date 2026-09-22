using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace JevMcp.App;

internal sealed class JevOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info ??= new OpenApiInfo();
        document.Info.Title = "JEV-MCP";
        document.Info.Version = "v1";
        document.Info.Description =
            "REST surface of the ten Jev judgment tools. Same JSON contract as MCP (`auto_accept`, `top_k`, …). " +
            "Authorize with the Bearer access token issued in Settings — the same token used on POST /mcp.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "API token",
            Description = "MCP access token. Header: Authorization: Bearer jevmcp_…",
        };

        if (document.Paths is not null)
        {
            foreach (var path in document.Paths.Keys.ToArray())
            {
                if (!Keep(path))
                {
                    document.Paths.Remove(path);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static bool Keep(string path)
    {
        return path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/jev", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class JevRestSecurityTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Description.RelativePath ?? "";
        if (!path.StartsWith("api/jev", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer")] = [],
        });
        return Task.CompletedTask;
    }
}
