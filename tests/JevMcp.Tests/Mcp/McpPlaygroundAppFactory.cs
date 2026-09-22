using JevMcp.Providers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JevMcp.Tests.Mcp;

public sealed class McpPlaygroundAppFactory : AccessAppFactory
{
    protected override void ConfigureAccessWebHost(IWebHostBuilder builder, string db)
    {
        base.ConfigureAccessWebHost(builder, db);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJevClient>();
            services.AddSingleton<IJevClient, PlaygroundMcpFakeJevClient>();
        });
    }
}
