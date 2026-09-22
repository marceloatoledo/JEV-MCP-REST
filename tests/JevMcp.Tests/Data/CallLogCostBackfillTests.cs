using JevMcp.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace JevMcp.Tests.Data;

public sealed class CallLogCostBackfillTests
{
    [Fact]
    public async Task BackfillComputesCostFromPersistedTokensAndProviderTariff()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);
        await settings.SetUsdPerMillionTokensAsync(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["typesafe"] = 0.1m,
        });

        await using (var db = harness.Factory.CreateDbContext())
        {
            db.CallLogs.Add(new CallLog
            {
                Instant = DateTime.UtcNow,
                Tool = "jev_screen",
                Provider = "typesafe",
                Status = AuditText.StatusOk,
                InputTokens = 1_000_000,
                OutputTokens = 0,
                CostUsd = 0,
            });
            await db.SaveChangesAsync();
        }

        var updated = await CallLogCostBackfill.RunAsync(
            harness.Factory,
            settings,
            NullLogger.Instance);

        Assert.Equal(1, updated);
        var log = Assert.Single(await harness.ListAsync());
        Assert.Equal(0.1m, log.CostUsd);
    }

    [Fact]
    public async Task BackfillSkipsRowsThatAlreadyHaveCost()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);

        const decimal existing = 0.0000005880m;
        await using (var db = harness.Factory.CreateDbContext())
        {
            db.CallLogs.Add(new CallLog
            {
                Instant = DateTime.UtcNow,
                Tool = "jev_screen",
                Provider = "typesafe",
                Status = AuditText.StatusOk,
                InputTokens = 14,
                OutputTokens = 0,
                CostUsd = existing,
            });
            await db.SaveChangesAsync();
        }

        var updated = await CallLogCostBackfill.RunAsync(harness.Factory, settings, NullLogger.Instance);

        Assert.Equal(0, updated);
        Assert.Equal(existing, Assert.Single(await harness.ListAsync()).CostUsd);
    }
}
