using System.Security.Cryptography;
using System.Text;

namespace JevMcp.Data;

/// <summary>Token generation and hash. No salt: entropy comes from the generator, not a human password.</summary>
internal static class AccessTokenFormat
{
    public const string GeneratedPrefix = "jevmcp_";

    public const string LegacyGeneratedPrefix = "javmcp_";

    public const int VisiblePrefixLength = 12;

    public const int SecretBytes = 24;

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        return GeneratedPrefix + ToBase64Url(bytes);
    }

    public static string VisiblePrefix(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if ((secret.StartsWith(GeneratedPrefix, StringComparison.Ordinal) ||
             secret.StartsWith(LegacyGeneratedPrefix, StringComparison.Ordinal)) &&
            secret.Length >= VisiblePrefixLength)
        {
            return secret[..VisiblePrefixLength];
        }

        // Arbitrary bootstrap secret: the prefix cannot be the value itself.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return "boot_" + Convert.ToHexString(hash)[..4].ToLowerInvariant();
    }

    public static byte[] Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public static string HashHex(string secret) => Convert.ToHexString(Hash(secret)).ToLowerInvariant();

    public static bool FixedEqualsHex(string storedHex, byte[] presentedHash)
    {
        ArgumentNullException.ThrowIfNull(storedHex);
        ArgumentNullException.ThrowIfNull(presentedHash);

        byte[] stored;
        try
        {
            stored = Convert.FromHexString(storedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return stored.Length == presentedHash.Length &&
            CryptographicOperations.FixedTimeEquals(stored, presentedHash);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
