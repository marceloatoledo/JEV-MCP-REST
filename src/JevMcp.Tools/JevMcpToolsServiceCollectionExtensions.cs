using JevMcp.Providers;
using JevMcp.Tools.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace JevMcp.Tools;

/// <summary>Registration of the judgment tools and the transport they use.</summary>
public static class JevMcpToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Jev transport, the judgment services, and the MCP shells. The services
    /// are the logic entry point: the admin playground calls the same objects, without going
    /// through the protocol.
    /// </summary>
    public static IServiceCollection AddJevMcpTools(this IServiceCollection services, JevProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddJevProviders(options);

        services.AddSingleton<VerifyService>();
        services.AddSingleton<ScreenService>();
        services.AddSingleton<FindService>();
        services.AddSingleton<ClassifyService>();
        services.AddSingleton<DecideService>();
        services.AddSingleton<RerankService>();
        services.AddSingleton<CompareService>();
        services.AddSingleton<ExtractService>();
        services.AddSingleton<ReviewService>();
        services.AddSingleton<GateService>();

        services.AddSingleton<JevVerifyTool>();
        services.AddSingleton<JevScreenTool>();
        services.AddSingleton<JevFindTool>();
        services.AddSingleton<JevClassifyTool>();
        services.AddSingleton<JevDecideTool>();
        services.AddSingleton<JevRerankTool>();
        services.AddSingleton<JevCompareTool>();
        services.AddSingleton<JevExtractTool>();
        services.AddSingleton<JevReviewTool>();
        services.AddSingleton<JevGateTool>();

        return services;
    }

    /// <summary>Registers the tools with the provider configuration read from the environment.</summary>
    public static IServiceCollection AddJevMcpTools(this IServiceCollection services)
    {
        return services.AddJevMcpTools(JevProviderOptions.FromEnvironment());
    }
}
