using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace JevMcp.Data;

internal sealed class OperationalSettings : IOperationalSettings
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly object _pricingLock = new();
    private int _allowAnonymousMcp;
    private int _capturePayloads;
    private int _retentionDays = 30;
    private string _displayTimeZoneId = DisplaySettings.DefaultTimeZoneId;
    private Dictionary<string, decimal> _usdPerMillionByProvider = new(StringComparer.OrdinalIgnoreCase);

    public OperationalSettings(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    public bool AllowAnonymousMcp => Volatile.Read(ref _allowAnonymousMcp) != 0;

    public bool CapturePayloads => Volatile.Read(ref _capturePayloads) != 0;

    public int RetentionDays => Volatile.Read(ref _retentionDays);

    public string DisplayTimeZoneId => Volatile.Read(ref _displayTimeZoneId);

    public IReadOnlyDictionary<string, decimal> UsdPerMillionByProvider
    {
        get
        {
            lock (_pricingLock)
            {
                return new Dictionary<string, decimal>(_usdPerMillionByProvider, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public decimal GetUsdPerMillionTokens(string? provider)
    {
        lock (_pricingLock)
        {
            if (!string.IsNullOrWhiteSpace(provider) &&
                _usdPerMillionByProvider.TryGetValue(provider, out var rate))
            {
                return rate;
            }

            return TokenPricing.DefaultUsdPerMillion;
        }
    }

    public event Action? Changed;

    public async Task EnsureDefaultsAsync(
        bool allowAnonymousMcp,
        bool capturePayloads,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await EnsureKeyAsync(
            db,
            AccessControlOptions.AllowAnonymousKey,
            allowAnonymousMcp ? "true" : "false",
            cancellationToken).ConfigureAwait(false);
        await EnsureKeyAsync(
            db,
            CallAuditOptions.CapturePayloadsKey,
            capturePayloads ? "true" : "false",
            cancellationToken).ConfigureAwait(false);
        await EnsureKeyAsync(
            db,
            CallAuditOptions.RetentionDaysKey,
            retentionDays.ToString(),
            cancellationToken).ConfigureAwait(false);
        await EnsureKeyAsync(
            db,
            DisplaySettings.TimeZoneKey,
            DisplaySettings.DefaultTimeZoneId,
            cancellationToken).ConfigureAwait(false);

        var legacy = await ReadAsync(db, TokenPricing.LegacyUsdPerMillionKey, cancellationToken)
            .ConfigureAwait(false);
        var hasRates = await db.Settings
            .AnyAsync(item => item.Key.StartsWith(TokenPricing.SettingKeyPrefix), cancellationToken)
            .ConfigureAwait(false);
        if (!hasRates)
        {
            var seed = legacy is not null
                ? ParseRate(legacy, TokenPricing.DefaultUsdPerMillion)
                : TokenPricing.DefaultUsdPerMillion;
            foreach (var slug in TokenPricing.ProviderSlugs)
            {
                await EnsureKeyAsync(
                    db,
                    TokenPricing.SettingKey(slug),
                    FormatRate(seed),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        WriteFlag(ref _allowAnonymousMcp, IsTrue(await ReadAsync(db, AccessControlOptions.AllowAnonymousKey, cancellationToken).ConfigureAwait(false)));
        WriteFlag(ref _capturePayloads, IsTrue(await ReadAsync(db, CallAuditOptions.CapturePayloadsKey, cancellationToken).ConfigureAwait(false)));
        Volatile.Write(
            ref _retentionDays,
            ParseDays(await ReadAsync(db, CallAuditOptions.RetentionDaysKey, cancellationToken).ConfigureAwait(false), retentionDays));
        Volatile.Write(
            ref _displayTimeZoneId,
            ParseTimeZoneId(await ReadAsync(db, DisplaySettings.TimeZoneKey, cancellationToken).ConfigureAwait(false)));
        WriteRates(await LoadRatesAsync(db, cancellationToken).ConfigureAwait(false));
    }

    public Task SetAllowAnonymousMcpAsync(bool value, CancellationToken cancellationToken = default)
    {
        WriteFlag(ref _allowAnonymousMcp, value);
        return PersistAsync(AccessControlOptions.AllowAnonymousKey, value ? "true" : "false", cancellationToken);
    }

    public Task SetCapturePayloadsAsync(bool value, CancellationToken cancellationToken = default)
    {
        WriteFlag(ref _capturePayloads, value);
        return PersistAsync(CallAuditOptions.CapturePayloadsKey, value ? "true" : "false", cancellationToken);
    }

    public Task SetRetentionDaysAsync(int value, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Volatile.Write(ref _retentionDays, value);
        return PersistAsync(CallAuditOptions.RetentionDaysKey, value.ToString(), cancellationToken);
    }

    public Task SetDisplayTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default)
    {
        var normalized = DisplayTimeZone.NormalizeId(timeZoneId);
        Volatile.Write(ref _displayTimeZoneId, normalized);
        return PersistAsync(DisplaySettings.TimeZoneKey, normalized, cancellationToken);
    }

    public async Task SetUsdPerMillionTokensAsync(
        IReadOnlyDictionary<string, decimal> ratesByProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ratesByProvider);

        var merged = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (slug, rate) in ratesByProvider)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rate);
            merged[TokenPricing.NormalizeSlug(slug)] = rate;
        }

        WriteRates(merged);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.Settings
            .Where(item => item.Key.StartsWith(TokenPricing.SettingKeyPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in existing)
        {
            if (!TokenPricing.TrySlugFromKey(row.Key, out var slug) || !merged.ContainsKey(slug))
            {
                db.Settings.Remove(row);
            }
        }

        foreach (var (slug, rate) in merged)
        {
            await UpsertRowAsync(db, slug, rate, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task UpsertUsdPerMillionTokensAsync(
        string provider,
        decimal rate,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rate);
        var slug = TokenPricing.NormalizeSlug(provider);

        lock (_pricingLock)
        {
            _usdPerMillionByProvider[slug] = rate;
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await UpsertRowAsync(db, slug, rate, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task DeleteUsdPerMillionTokensAsync(string provider, CancellationToken cancellationToken = default)
    {
        var slug = TokenPricing.NormalizeSlug(provider);
        lock (_pricingLock)
        {
            _usdPerMillionByProvider.Remove(slug);
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var key = TokenPricing.SettingKey(slug);
        var setting = await db.Settings
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken)
            .ConfigureAwait(false);
        if (setting is not null)
        {
            db.Settings.Remove(setting);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        Changed?.Invoke();
    }

    private static async Task UpsertRowAsync(
        AppDbContext db,
        string slug,
        decimal rate,
        CancellationToken cancellationToken)
    {
        var key = TokenPricing.SettingKey(slug);
        var value = FormatRate(rate);
        var setting = await db.Settings
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (setting is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
    }

    private async Task PersistAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var setting = await db.Settings
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (setting is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    private static async Task EnsureKeyAsync(
        AppDbContext db,
        string key,
        string seed,
        CancellationToken cancellationToken)
    {
        var exists = await db.Settings.AnyAsync(item => item.Key == key, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = seed });
        }
    }

    private static async Task<string?> ReadAsync(AppDbContext db, string key, CancellationToken cancellationToken)
    {
        var setting = await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Key == key, cancellationToken)
            .ConfigureAwait(false);
        return setting?.Value;
    }

    private static async Task<Dictionary<string, decimal>> LoadRatesAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var rows = await db.Settings.AsNoTracking()
            .Where(item => item.Key.StartsWith(TokenPricing.SettingKeyPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (!TokenPricing.TrySlugFromKey(row.Key, out var slug))
            {
                continue;
            }

            rates[slug] = ParseRate(row.Value, TokenPricing.DefaultUsdPerMillion);
        }

        return rates;
    }

    private static void WriteFlag(ref int field, bool value) => Volatile.Write(ref field, value ? 1 : 0);

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static int ParseDays(string? value, int fallback) =>
        int.TryParse(value, out var days) && days >= 0 ? days : fallback;

    private static string ParseTimeZoneId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DisplaySettings.DefaultTimeZoneId;
        }

        try
        {
            return DisplayTimeZone.NormalizeId(value);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return DisplaySettings.DefaultTimeZoneId;
        }
    }

    private void WriteRates(Dictionary<string, decimal> rates)
    {
        lock (_pricingLock)
        {
            _usdPerMillionByProvider = new Dictionary<string, decimal>(rates, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string FormatRate(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);

    private static decimal ParseRate(string? value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) && rate >= 0
            ? rate
            : fallback;
}
