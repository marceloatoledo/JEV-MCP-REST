using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JevMcp.Data;

/// <summary>
/// A single reader persists what several tools enqueue. The call never waits for
/// <c>SaveChanges</c>: a full disk becomes a log, not a tool error.
/// </summary>
internal sealed class CallLogWriter : BackgroundService, ICallLogSink
{
    private readonly Channel<CallLog> _channel = Channel.CreateUnbounded<CallLog>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<CallLogWriter> _logger;
    private int _outstanding;

    public CallLogWriter(IDbContextFactory<AppDbContext> factory, ILogger<CallLogWriter> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public void Enqueue(CallLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        Interlocked.Increment(ref _outstanding);
        try
        {
            if (!_channel.Writer.TryWrite(log))
            {
                Interlocked.Decrement(ref _outstanding);
                _logger.LogWarning("Call audit queue rejected a record for {Tool}.", log.Tool);
            }
        }
        catch (Exception exception)
        {
            Interlocked.Decrement(ref _outstanding);
            _logger.LogError(exception, "Call audit enqueue failed.");
        }
    }

    /// <summary>Waits for the queue to drain. Only tests need this.</summary>
    internal async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        while (Volatile.Read(ref _outstanding) > 0)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var log in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await using var db = await _factory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
                    db.CallLogs.Add(log);
                    await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Call audit persistence failed.");
                }
                finally
                {
                    Interlocked.Decrement(ref _outstanding);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Registers the writer as singleton and hosted service without duplicating the instance.</summary>
internal static class CallLogWriterRegistration
{
    public static IServiceCollection AddCallLogWriter(this IServiceCollection services)
    {
        services.AddSingleton<CallLogWriter>();
        services.AddSingleton<ICallLogSink>(provider => provider.GetRequiredService<CallLogWriter>());
        services.AddHostedService(provider => provider.GetRequiredService<CallLogWriter>());
        return services;
    }
}
