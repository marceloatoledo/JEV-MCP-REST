using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JevMcp.Data;

internal interface IAccessTokenTouchSink
{
    void Touch(long tokenId);
}

/// <summary>
/// Last use is approximate: at most one write per token per minute, off the request
/// path. Answering "is this token still used?" does not need second-level precision.
/// </summary>
internal sealed class AccessTokenTouchWriter : BackgroundService, IAccessTokenTouchSink
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly ConcurrentDictionary<long, DateTime> _lastQueued = new();
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<AccessTokenTouchWriter> _logger;

    public AccessTokenTouchWriter(IDbContextFactory<AppDbContext> factory, ILogger<AccessTokenTouchWriter> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public void Touch(long tokenId)
    {
        var minute = FloorToMinute(DateTime.UtcNow);
        if (!_lastQueued.TryAdd(tokenId, minute) && _lastQueued[tokenId] >= minute)
        {
            return;
        }

        _lastQueued[tokenId] = minute;
        _channel.Writer.TryWrite(tokenId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var at = FloorToMinute(DateTime.UtcNow);
                    await using var db = await _factory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
                    await db.AccessTokens
                        .Where(token => token.Id == id)
                        .ExecuteUpdateAsync(
                            update => update.SetProperty(token => token.LastUsedAt, at),
                            stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(exception, "Failed to record last use of access token {TokenId}.", id);
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

    private static DateTime FloorToMinute(DateTime utc)
    {
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }
}
