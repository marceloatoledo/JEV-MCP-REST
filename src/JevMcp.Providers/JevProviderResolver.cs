namespace JevMcp.Providers;

/// <summary>
/// Chosen provider, or the reason no choice is possible. Without a credential the
/// application starts and reports the problem; the failure shows up in `/health` and
/// admin, not on the first tool an agent calls.
/// </summary>
public sealed record JevProviderResolution(JevProviderKind? Provider, string? Error)
{
    public bool IsConfigured => Provider is not null;

    /// <summary>The resolved provider, or the configuration exception with the resolver's message.</summary>
    public JevProviderKind Require()
    {
        return Provider ?? throw new JevConfigurationException(
            Error ?? "No Jev provider is configured.");
    }
}

/// <summary>
/// Provider selection. Automatic precedence and error messages match the original,
/// so its documentation still applies.
/// </summary>
public static class JevProviderResolver
{
    private const string NoCredentials =
        "No Jev provider credentials found. Set TYPESAFE_API_KEY, OPENROUTER_API_KEY (sk-or-), " +
        "Cloudflare token + CLOUDFLARE_ACCOUNT_ID, AI_GATEWAY_API_KEY, or JEV_API_KEY + JEV_API_BASE_URL; " +
        "set JEV_PROVIDER to choose explicitly.";

    public static JevProviderResolution Resolve(JevProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hasTypeSafe = !string.IsNullOrEmpty(options.TypeSafeApiKey);
        var hasOpenRouter = options.OpenRouterApiKey?.StartsWith("sk-or-", StringComparison.Ordinal) == true;
        var hasCloudflare = !string.IsNullOrEmpty(options.CloudflareApiToken) &&
            !string.IsNullOrEmpty(options.CloudflareAccountId);
        var hasVercel = !string.IsNullOrEmpty(options.AiGatewayApiKey);
        var hasCompatible = !string.IsNullOrEmpty(options.CompatibleApiKey) &&
            !string.IsNullOrEmpty(options.CompatibleBaseUrl);

        var explicitProvider = options.Provider?.ToLowerInvariant() ?? "auto";

        switch (explicitProvider)
        {
            case "typesafe":
                return hasTypeSafe
                    ? new JevProviderResolution(JevProviderKind.TypeSafe, null)
                    : Failure("JEV_PROVIDER=typesafe but TYPESAFE_API_KEY is not set.");

            case "openrouter":
                return hasOpenRouter
                    ? new JevProviderResolution(JevProviderKind.OpenRouter, null)
                    : Failure("JEV_PROVIDER=openrouter but OPENROUTER_API_KEY is not set or not an sk-or- key.");

            case "vercel":
                return hasVercel
                    ? new JevProviderResolution(JevProviderKind.Vercel, null)
                    : Failure("JEV_PROVIDER=vercel but AI_GATEWAY_API_KEY is not set.");

            case "cloudflare":
                return hasCloudflare
                    ? new JevProviderResolution(JevProviderKind.Cloudflare, null)
                    : Failure(
                        "JEV_PROVIDER=cloudflare but a Cloudflare API token (CLOUDFLARE_API_TOKEN or " +
                        "JEV_CLOUDFLARE_API_TOKEN) and CLOUDFLARE_ACCOUNT_ID are not both set.");

            case "compatible":
                return hasCompatible
                    ? new JevProviderResolution(JevProviderKind.Compatible, null)
                    : Failure(MissingCompatible(options));

            case "auto":
                break;

            default:
                // The original would fall through to automatic detection here. A wrong name would
                // pass silently and the operator would see a provider they did not ask for.
                return Failure(
                    $"JEV_PROVIDER={options.Provider} is not a known provider. " +
                    "Use typesafe, openrouter, cloudflare, vercel, compatible, or auto.");
        }

        if (hasTypeSafe)
        {
            return new JevProviderResolution(JevProviderKind.TypeSafe, null);
        }

        if (hasOpenRouter)
        {
            return new JevProviderResolution(JevProviderKind.OpenRouter, null);
        }

        if (hasCloudflare)
        {
            return new JevProviderResolution(JevProviderKind.Cloudflare, null);
        }

        if (hasVercel)
        {
            return new JevProviderResolution(JevProviderKind.Vercel, null);
        }

        if (hasCompatible)
        {
            return new JevProviderResolution(JevProviderKind.Compatible, null);
        }

        return Failure(NoCredentials);
    }

    private static JevProviderResolution Failure(string error) => new(null, error);

    private static string MissingCompatible(JevProviderOptions options)
    {
        var missing = new List<string>(2);
        if (string.IsNullOrEmpty(options.CompatibleApiKey))
        {
            missing.Add("JEV_API_KEY");
        }

        if (string.IsNullOrEmpty(options.CompatibleBaseUrl))
        {
            missing.Add("JEV_API_BASE_URL");
        }

        var verb = missing.Count > 1 ? "are" : "is";
        return $"JEV_PROVIDER=compatible but {string.Join(" and ", missing)} {verb} not set. " +
            "JEV_MCP_MODEL is optional and defaults to jev-latest.";
    }
}
