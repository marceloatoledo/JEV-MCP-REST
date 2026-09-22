using JevMcp.Providers;

namespace JevMcp.App;

internal static class JevProviderConfiguration
{
    public static JevProviderOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new JevProviderOptions();
        configuration.GetSection(JevProviderOptions.SectionName).Bind(options);
        var typeSafe = new TypeSafeOptions();
        configuration.GetSection(TypeSafeOptions.SectionName).Bind(typeSafe);
        typeSafe.CopyTo(options);
        var openRouter = new OpenRouterOptions();
        configuration.GetSection(OpenRouterOptions.SectionName).Bind(openRouter);
        openRouter.CopyTo(options);
        var cloudflare = new CloudflareOptions();
        configuration.GetSection(CloudflareOptions.SectionName).Bind(cloudflare);
        cloudflare.CopyTo(options);
        var aiGateway = new AiGatewayOptions();
        configuration.GetSection(AiGatewayOptions.SectionName).Bind(aiGateway);
        aiGateway.CopyTo(options);
        var compatible = new CompatibleOptions();
        configuration.GetSection(CompatibleOptions.SectionName).Bind(compatible);
        compatible.CopyTo(options);
        JevProviderOptions.FillNamedEnvironment(options, Environment.GetEnvironmentVariable);
        return options;
    }
}
