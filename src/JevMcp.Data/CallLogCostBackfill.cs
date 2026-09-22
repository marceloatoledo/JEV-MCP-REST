using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JevMcp.Data;

/// <summary>
/// Fills <see cref="CallLog.CostUsd"/> for rows written before costing existed.
/// Uses the current tariff per <see cref="CallLog.Provider"/> (no historical price).
/// </summary>
public static class CallLogCostBackfill
{
    public static async Task<int> RunAsync(
        IDbContextFactory<AppDbContext> factory,
        IOperationalSettings settings,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.CallLogs
            .Where(log => log.CostUsd == 0m && log.InputTokens + log.OutputTokens > 0)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var log in rows)
        {
            log.CostUsd = TokenCost.ComputeUsd(
                log.InputTokens,
                log.OutputTokens,
                settings.GetUsdPerMillionTokens(log.Provider));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Backfilled CostUsd for {Count} historical call log(s).", rows.Count);
        return rows.Count;
    }
}
