using JevMcp.Data;
using JevMcp.Providers;
using Microsoft.EntityFrameworkCore;

namespace JevMcp.Tests.Data;

public sealed class TokenPricingTests
{
    [Fact]
    public void ProviderSlugsCoverEveryJevBackend()
    {
        Assert.Equal(
            Enum.GetValues<JevProviderKind>().Select(kind => kind.ToString().ToLowerInvariant()).Order(StringComparer.Ordinal),
            TokenPricing.ProviderSlugs);
    }

    [Fact]
    public void SettingKeyIsTheSqliteRowName()
    {
        Assert.Equal("token_usd_per_million.typesafe", TokenPricing.SettingKey("typesafe"));
        Assert.Equal(0.042m, TokenPricing.DefaultUsdPerMillion);
    }

    [Fact]
    public void NormalizeSlugAcceptsCustomProviders()
    {
        Assert.Equal("acme-ai", TokenPricing.NormalizeSlug(" Acme-AI "));
        Assert.False(TokenPricing.TryNormalizeSlug("Acme AI", out _));
        Assert.False(TokenPricing.TryNormalizeSlug("", out _));
    }
}

public sealed class TokenPricingPersistenceTests
{
    [Fact]
    public async Task FirstStartSeedsDefaultRatesIntoSqlite()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);

        await using var db = await harness.Factory.CreateDbContextAsync();
        foreach (var slug in TokenPricing.ProviderSlugs)
        {
            Assert.Equal(TokenPricing.DefaultUsdPerMillion, settings.GetUsdPerMillionTokens(slug));
            var row = await db.Settings.SingleAsync(item => item.Key == TokenPricing.SettingKey(slug));
            Assert.Equal("0.042", row.Value);
        }
    }

    [Fact]
    public async Task SavedRatesSurviveANewProcessAndIgnoreMissingConfig()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);
        await settings.SetUsdPerMillionTokensAsync(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["typesafe"] = 0.12m,
            ["openrouter"] = 0.08m,
        });

        var reload = new OperationalSettings(harness.Factory);
        await reload.EnsureDefaultsAsync(false, false, 30);

        Assert.Equal(0.12m, reload.GetUsdPerMillionTokens("typesafe"));
        Assert.Equal(0.08m, reload.GetUsdPerMillionTokens("openrouter"));
        Assert.Equal(TokenPricing.DefaultUsdPerMillion, reload.GetUsdPerMillionTokens("cloudflare"));
    }

    [Fact]
    public async Task CustomProviderPersistsAndUnknownFallsBackToDefault()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);
        await settings.UpsertUsdPerMillionTokensAsync("acme-ai", 0.2m);

        var reload = new OperationalSettings(harness.Factory);
        await reload.EnsureDefaultsAsync(false, false, 30);

        Assert.Equal(0.2m, reload.GetUsdPerMillionTokens("acme-ai"));
        Assert.Contains("acme-ai", reload.UsdPerMillionByProvider.Keys);
    }

    [Fact]
    public async Task DeletedProviderIsNotReseeded()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);
        await settings.DeleteUsdPerMillionTokensAsync("typesafe");

        var reload = new OperationalSettings(harness.Factory);
        await reload.EnsureDefaultsAsync(false, false, 30);

        Assert.False(reload.UsdPerMillionByProvider.ContainsKey("typesafe"));
        Assert.Equal(TokenPricing.DefaultUsdPerMillion, reload.GetUsdPerMillionTokens("typesafe"));
        await using var db = await harness.Factory.CreateDbContextAsync();
        Assert.False(await db.Settings.AnyAsync(item => item.Key == TokenPricing.SettingKey("typesafe")));
    }

    [Fact]
    public async Task UpsertRejectsInvalidSlug()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);

        await Assert.ThrowsAsync<ArgumentException>(() => settings.UpsertUsdPerMillionTokensAsync("Not A Slug", 0.1m));
    }

    [Fact]
    public async Task LegacySingleRateSeedsProvidersThatHaveNoRowYet()
    {
        await using var harness = await AuditHarness.CreateAsync();
        await using (var db = await harness.Factory.CreateDbContextAsync())
        {
            db.Settings.Add(new AppSetting { Key = TokenPricing.LegacyUsdPerMillionKey, Value = "0.099" });
            await db.SaveChangesAsync();
        }

        var settings = new OperationalSettings(harness.Factory);
        await settings.EnsureDefaultsAsync(false, false, 30);

        Assert.Equal(0.099m, settings.GetUsdPerMillionTokens("typesafe"));
        Assert.Equal(0.099m, settings.GetUsdPerMillionTokens("vercel"));
    }
}
