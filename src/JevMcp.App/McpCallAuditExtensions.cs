using System.Text.Json;
using JevMcp.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JevMcp.App;

/// <summary>
/// Loads the tool name and the MCP result action into the audit scope. Tool shells
/// do not need to know that the database exists.
/// </summary>
internal static class McpCallAuditExtensions
{
    public static IMcpRequestFilterBuilder AddCallAudit(this IMcpRequestFilterBuilder filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        filters.AddCallToolFilter(next => async (request, cancellationToken) =>
        {
            var audit = request.Services?.GetService<ICallAudit>();
            if (audit is null)
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }

            using var scope = audit.Begin(request.Params?.Name ?? "");
            var user = request.User ?? request.Services?.GetService<IHttpContextAccessor>()?.HttpContext?.User;
            CallAuditOrigin.Apply(scope, user);
            if (request.Params?.Arguments is { } arguments)
            {
                scope.SetRequestPayload(JsonSerializer.Serialize(arguments));
            }

            try
            {
                var result = await next(request, cancellationToken).ConfigureAwait(false);
                var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                scope.Complete(text, result.IsError is true);
                return result;
            }
            catch (Exception exception)
            {
                scope.Complete(exception.Message, isError: true);
                throw;
            }
        });

        return filters;
    }
}
