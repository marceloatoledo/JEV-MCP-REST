using JevMcp.Data;
using JevMcp.Providers;

namespace JevMcp.App;

/// <summary>
/// View of credentials the UI may show. The full value never enters this type.
/// </summary>
public sealed class CredentialOverview
{
    public CredentialOverview(JevProviderOptions options, JevProviderResolution resolution, AccessControlOptions access)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(access);

        Model = options.Model;
        Provider = resolution.Provider?.ToString();
        Error = resolution.Error;
        IsConfigured = resolution.IsConfigured;
        Credentials =
        [
            SecretMask.Describe("TYPESAFE__API__KEY", options.TypeSafeApiKey),
            SecretMask.Describe("OPENROUTER__API__KEY", options.OpenRouterApiKey),
            SecretMask.Describe("CLOUDFLARE__API__TOKEN", options.CloudflareApiToken),
            SecretMask.Describe("CLOUDFLARE__ACCOUNT__ID", options.CloudflareAccountId),
            SecretMask.Describe("AIGATEWAY__API__KEY", options.AiGatewayApiKey),
            SecretMask.Describe("COMPATIBLE__API__KEY", options.CompatibleApiKey),
            SecretMask.DescribeVisible(AccessControlOptions.UsernameEnvironmentKey, access.Username),
            SecretMask.Describe(AccessControlOptions.PasswordEnvironmentKey, access.Password),
        ];
    }

    public string Model { get; }

    public string? Provider { get; }

    public string? Error { get; }

    public bool IsConfigured { get; }

    public IReadOnlyList<MaskedCredential> Credentials { get; }
}

public sealed record MaskedCredential(string Variable, bool Present, string Display);

public static class SecretMask
{
    public static MaskedCredential Describe(string variable, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return new MaskedCredential(variable, Present: false, "—");
        }

        return new MaskedCredential(variable, Present: true, Mask(secret));
    }

    public static MaskedCredential DescribeVisible(string variable, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new MaskedCredential(variable, Present: false, "—");
        }

        return new MaskedCredential(variable, Present: true, value);
    }

    public static string Mask(string secret)
    {
        if (secret.Length <= 8)
        {
            return "••••••••";
        }

        return string.Concat(secret.AsSpan(0, 4), "••••");
    }
}
