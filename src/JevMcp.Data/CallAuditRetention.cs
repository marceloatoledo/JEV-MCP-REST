using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JevMcp.Data;

/// <summary>Age-based purge. Without this the volume grows until it fills the operator disk.</summary>
internal sealed class CallAuditRetention
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IOptionsMonitor<CallAuditOptions> _options;
    private readonly ILogger<CallAuditRetention> _logger;
    private readonly IOperationalSettings? _settings;

    public CallAuditRetention(
        IDbContextFactory<AppDbContext> factory,
        IOptionsMonitor<CallAuditOptions> options,
        ILogger<CallAuditRetention> logger,
        IOperationalSettings? settings = null)
    {
        _factory = factory;
        _options = options;
        _logger = logger;
        _settings = settings;
    }

    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var days = CallAuditOptionsOverlay.Effective(_options.CurrentValue, _settings).RetentionDays;
        if (days < 1)
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddDays(-days);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var removed = await db.CallLogs
            .Where(log => log.Instant < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (removed > 0)
        {
            _logger.LogInformation(
                "Call audit retention removed {Count} records older than {Days} days.",
                removed,
                days);
        }

        return removed;
    }
}

internal sealed class CallAuditRetentionService : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromHours(6);

    private readonly CallAuditRetention _retention;
    private readonly ILogger<CallAuditRetentionService> _logger;

    public CallAuditRetentionService(CallAuditRetention retention, ILogger<CallAuditRetentionService> logger)
    {
        _retention = retention;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);

        do
        {
            try
            {
                await _retention.PurgeAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Call audit retention failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
