using System.Text.RegularExpressions;
using JevMcp.Providers;

namespace JevMcp.Data;

/// <summary>
/// Per-provider USD rates for audit cost estimates. Values live in SQLite
/// (<c>token_usd_per_million.&lt;slug&gt;</c>); this type names keys, the first-run seed, and slug rules.
/// </summary>
public static partial class TokenPricing
{
    /// <summary>Legacy single-rate key; used only to seed per-provider rows on first upgrade.</summary>
    public const string LegacyUsdPerMillionKey = "token_usd_per_million";

    public const string SettingKeyPrefix = "token_usd_per_million.";

    public const decimal DefaultUsdPerMillion = 0.042m;

    public const int SlugMaxLength = 32;

    public static IReadOnlyList<string> ProviderSlugs { get; } =
        Enum.GetValues<JevProviderKind>()
            .Select(kind => Slug(kind))
            .OrderBy(slug => slug, StringComparer.Ordinal)
            .ToArray();

    public static string Slug(JevProviderKind kind) =>
        kind.ToString().ToLowerInvariant();

    public static string SettingKey(string providerSlug) =>
        SettingKeyPrefix + NormalizeSlug(providerSlug);

    public static bool TrySlugFromKey(string key, out string slug)
    {
        slug = "";
        if (string.IsNullOrEmpty(key) ||
            !key.StartsWith(SettingKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return TryNormalizeSlug(key[SettingKeyPrefix.Length..], out slug);
    }

    public static string NormalizeSlug(string? value)
    {
        if (!TryNormalizeSlug(value, out var slug))
        {
            throw new ArgumentException(
                "provider must match ^[a-z][a-z0-9_-]*$ and be at most 32 characters.",
                nameof(value));
        }

        return slug;
    }

    public static bool TryNormalizeSlug(string? value, out string slug)
    {
        slug = (value ?? "").Trim().ToLowerInvariant();
        return slug.Length is > 0 and <= SlugMaxLength && SlugPattern().IsMatch(slug);
    }

    public static bool IsKnownProvider(string? providerSlug) =>
        TryNormalizeSlug(providerSlug, out var slug) &&
        ProviderSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,31}$")]
    private static partial Regex SlugPattern();
}
