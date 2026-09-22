using JevMcp.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JevMcp.Data;

/// <summary>Registers audit in the container. Must come after <c>AddJevMcpTools</c>.</summary>
public static class JevMcpDataServiceCollectionExtensions
{
    public static IServiceCollection AddJevMcpData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CallAuditOptions>().Bind(configuration.GetSection(CallAuditOptions.SectionName));
        services.AddOptions<AccessControlOptions>()
            .Bind(configuration.GetSection(AccessControlOptions.SectionName))
            .PostConfigure(options =>
                AccessControlOptions.FillNamedEnvironment(options, Environment.GetEnvironmentVariable));
        return services.AddJevMcpDataCore();
    }

    public static IServiceCollection AddJevMcpData(this IServiceCollection services, Action<CallAuditOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<CallAuditOptions>().Configure(configure);
        services.AddOptions<AccessControlOptions>();
        return services.AddJevMcpDataCore();
    }

    private static IServiceCollection AddJevMcpDataCore(this IServiceCollection services)
    {
        services.AddSingleton<SqliteWalInterceptor>();
        services.AddDbContextFactory<AppDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<IOptions<CallAuditOptions>>().Value;
            var path = ResolveDatabasePath(options.DatabasePath, provider.GetService<IHostEnvironment>());
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            builder.UseSqlite($"Data Source={path}");
            builder.AddInterceptors(provider.GetRequiredService<SqliteWalInterceptor>());
        });

        services.AddCallLogWriter();
        services.AddSingleton<CallAuditRetention>();
        services.AddHostedService<CallAuditRetentionService>();
        services.AddSingleton<ICallAudit, CallAudit>();
        services.AddSingleton<ICallLogQueries, CallLogQueries>();
        services.AddSingleton<ICallAuditAdmin, CallAuditAdmin>();
        services.AddSingleton<AccessTokenTouchWriter>();
        services.AddSingleton<IAccessTokenTouchSink>(provider => provider.GetRequiredService<AccessTokenTouchWriter>());
        services.AddHostedService(provider => provider.GetRequiredService<AccessTokenTouchWriter>());
        services.AddSingleton<IAccessTokenService, AccessTokenService>();
        services.AddSingleton<IOperationalSettings, OperationalSettings>();
        DecorateJevClient(services);
        return services;
    }

    /// <summary>Applies versioned migrations. With no file, SQLite creates the database at the configured path.</summary>
    public static IHost InitializeCallAudit(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var db = host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        db.Database.Migrate();

        var options = host.Services.GetRequiredService<IOptions<CallAuditOptions>>().Value;
        var environment = host.Services.GetService<IHostEnvironment>();
        var path = ResolveDatabasePath(options.DatabasePath, environment);
        if (SqliteStorageGuard.ShouldWarnEphemeral(path))
        {
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("JevMcp.Data");
            logger.LogWarning(SqliteStorageGuard.EphemeralWarning, path);
        }

        return host;
    }

    internal static string ResolveDatabasePath(string? configured, IHostEnvironment? environment)
    {
        var path = string.IsNullOrWhiteSpace(configured) ? "data/jevmcp.db" : configured;
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var root = environment?.ContentRootPath ?? AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static void DecorateJevClient(IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(item => item.ServiceType == typeof(IJevClient))
            ?? throw new InvalidOperationException("AddJevMcpData requires AddJevMcpTools to run first.");

        services.Remove(descriptor);
        services.AddSingleton<IJevClient>(provider =>
        {
            var inner = CreateInstance<IJevClient>(provider, descriptor);
            return new AuditingJevClient(
                inner,
                provider.GetRequiredService<ICallLogSink>(),
                provider.GetRequiredService<IOptionsMonitor<CallAuditOptions>>(),
                provider.GetRequiredService<JevProviderOptions>(),
                provider.GetRequiredService<JevProviderResolution>(),
                provider.GetRequiredService<ILogger<AuditingJevClient>>(),
                provider.GetRequiredService<IOperationalSettings>());
        });
    }

    private static TService CreateInstance<TService>(IServiceProvider provider, ServiceDescriptor descriptor)
        where TService : class
    {
        if (descriptor.ImplementationInstance is TService instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (TService)factory(provider);
        }

        return (TService)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}
