using JevMcp.Data;
using Microsoft.EntityFrameworkCore;

namespace JevMcp.Tests.Data;

public sealed class OperationalSettingsTests
{
    [Fact]
    public async Task ChangingRetentionAndCaptureTakesEffectWithoutRecreatingTheService()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(allowAnonymousMcp: false, capturePayloads: false, retentionDays: 30);

        Assert.False(settings.CapturePayloads);
        Assert.Equal(30, settings.RetentionDays);

        await settings.SetCapturePayloadsAsync(true);
        await settings.SetRetentionDaysAsync(7);
        await settings.SetUsdPerMillionTokensAsync(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["typesafe"] = 0.05m,
        });
        await settings.SetAllowAnonymousMcpAsync(true);

        Assert.True(settings.CapturePayloads);
        Assert.Equal(7, settings.RetentionDays);
        Assert.Equal(0.05m, settings.GetUsdPerMillionTokens("typesafe"));
        Assert.True(settings.AllowAnonymousMcp);

        var again = new OperationalSettings(harness.Factory);
        await again.EnsureDefaultsAsync(allowAnonymousMcp: false, capturePayloads: false, retentionDays: 30);
        Assert.True(again.CapturePayloads);
        Assert.Equal(7, again.RetentionDays);
        Assert.Equal(0.05m, again.GetUsdPerMillionTokens("typesafe"));
        Assert.True(again.AllowAnonymousMcp);
    }

    [Fact]
    public async Task DisplayTimeZonePersistsAndReloads()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);
        Assert.Equal(DisplaySettings.DefaultTimeZoneId, settings.DisplayTimeZoneId);

        var zoneId = TimeZoneInfo.GetSystemTimeZones().First(zone => !zone.Equals(TimeZoneInfo.Utc)).Id;
        await settings.SetDisplayTimeZoneIdAsync(zoneId);

        var reload = new OperationalSettings(harness.Factory);
        await reload.EnsureDefaultsAsync(false, false, 30);
        Assert.Equal(DisplayTimeZone.NormalizeId(zoneId), reload.DisplayTimeZoneId);
    }

    [Fact]
    public async Task EnsureDefaultsDoesNotOverwriteExistingTariff()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);
        await settings.SetUsdPerMillionTokensAsync(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["openrouter"] = 0.099m,
        });

        var reload = new OperationalSettings(harness.Factory);
        await reload.EnsureDefaultsAsync(false, false, 30);

        Assert.Equal(0.099m, reload.GetUsdPerMillionTokens("openrouter"));
    }
}
