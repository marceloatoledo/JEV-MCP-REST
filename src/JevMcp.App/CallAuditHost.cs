using JevMcp.Data;
using Microsoft.EntityFrameworkCore;

namespace JevMcp.App;

internal static class CallAuditHost
{
    public static async Task BackfillHistoricalCostsAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var factory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var settings = app.Services.GetRequiredService<IOperationalSettings>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("JevMcp.CallAudit");

        await CallLogCostBackfill.RunAsync(factory, settings, logger).ConfigureAwait(false);
    }
}
