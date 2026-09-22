using Microsoft.Extensions.DependencyInjection;

namespace JevMcp.Providers;

/// <summary>Registration of the Jev transport in the container.</summary>
public static class JevProviderServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> used by all adapters.</summary>
    public const string HttpClientName = "jev";

    /// <summary>
    /// Registers the options, provider resolution, and the client. Resolution happens
    /// here, at startup, not on the first tool call.
    /// </summary>
    public static IServiceCollection AddJevProviders(this IServiceCollection services, JevProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var resolution = JevProviderResolver.Resolve(options);

        services.AddSingleton(options);
        services.AddSingleton(resolution);

        services
            .AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60))
            .AddHttpMessageHandler(() => new JevRetryHandler(options.Retry));

        services.AddSingleton<IJevClient>(provider =>
        {
            if (resolution.Provider is not { } kind)
            {
                return new UnconfiguredJevClient(resolution);
            }

            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            return JevClients.Create(kind, http, options);
        });

        return services;
    }

    /// <summary>Registers the transport from the process environment variables.</summary>
    public static IServiceCollection AddJevProviders(this IServiceCollection services)
    {
        return services.AddJevProviders(JevProviderOptions.FromEnvironment());
    }
}
