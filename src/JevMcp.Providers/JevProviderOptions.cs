namespace JevMcp.Providers;

/// <summary>Retry limits. The defaults are conservative on purpose.</summary>
public sealed class JevRetryOptions
{
    /// <summary>Total attempts, including the first.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base wait for exponential backoff.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Cap on wait between attempts.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Provider configuration. Variable names match the original jev-mcp,
/// so an environment that works there works here without translation.
/// </summary>
public sealed class JevProviderOptions
{
    public const string SectionName = "JEV";

    public const string DefaultModel = "jev-latest";

    /// <summary>Origin of the direct endpoint; the `/v1/systemone` route is appended by the adapter.</summary>
    public const string DefaultTypeSafeBaseUrl = "https://api.typesafe.ai";

    public const string DefaultOpenRouterBaseUrl = "https://openrouter.ai/api/alpha/decisions";

    public const string DefaultCloudflareBaseUrl = "https://api.cloudflare.com/client/v4";

    public const string DefaultAiGatewayBaseUrl = "https://ai-gateway.vercel.sh/v4/ai";

    private JevMcpOptions _mcp = new();

    /// <summary>`JEV:PROVIDER` / `JEV__PROVIDER` (alias legado: `JEV_PROVIDER`).</summary>
    public string? Provider { get; set; }

    /// <summary>`JEV:MCP:MODEL` / `JEV__MCP__MODEL` (alias legado: `JEV_MCP_MODEL`).</summary>
    public JevMcpOptions Mcp
    {
        get => _mcp;
        set => _mcp = value ?? new JevMcpOptions();
    }

    /// <summary>Configured model id. Same as <see cref="JevMcpOptions.Model"/>.</summary>
    public string Model
    {
        get => string.IsNullOrWhiteSpace(Mcp.Model) ? DefaultModel : Mcp.Model;
        set => Mcp.Model = value;
    }

    /// <summary>`TYPESAFE__API__KEY` / <c>TYPESAFE:API:KEY</c> (legacy: `TYPESAFE_API_KEY`).</summary>
    public string? TypeSafeApiKey { get; set; }

    /// <summary>`TYPESAFE__BASEURL` / <c>TYPESAFE:BASEURL</c> (legacy: `TYPESAFE_BASE_URL`).</summary>
    public string? TypeSafeBaseUrl { get; set; }

    /// <summary>`OPENROUTER__API__KEY` / <c>OPENROUTER:API:KEY</c> (legacy: `OPENROUTER_API_KEY`).</summary>
    public string? OpenRouterApiKey { get; set; }

    /// <summary>`JEV_CLOUDFLARE_API_TOKEN` / <c>CLOUDFLARE:API:TOKEN</c>.</summary>
    public string? CloudflareApiToken { get; set; }

    /// <summary>`JEV_CLOUDFLARE_ACCOUNT_ID` / <c>CLOUDFLARE:ACCOUNT:ID</c>.</summary>
    public string? CloudflareAccountId { get; set; }

    /// <summary>`JEV_AI_GATEWAY_API_KEY` / <c>AIGATEWAY:API:KEY</c>.</summary>
    public string? AiGatewayApiKey { get; set; }

    /// <summary>`AI_GATEWAY_BASE_URL` / <c>AIGATEWAY:BASEURL</c>.</summary>
    public string? AiGatewayBaseUrl { get; set; }

    /// <summary>`JEV_API_KEY` / <c>COMPATIBLE:API:KEY</c>.</summary>
    public string? CompatibleApiKey { get; set; }

    /// <summary>`JEV_API_BASE_URL` / <c>COMPATIBLE:BASEURL</c> — full POST URL, used verbatim.</summary>
    public string? CompatibleBaseUrl { get; set; }

    public JevRetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Fills blank fields from named environment variables. Bound configuration
    /// (<c>appsettings</c>, <c>JEV__*</c>) wins when already set.
    /// </summary>
    public static void FillNamedEnvironment(JevProviderOptions options, Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lookup);

        options.Mcp ??= new JevMcpOptions();

        string? Read(string name) => string.IsNullOrWhiteSpace(lookup(name)) ? null : lookup(name);

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            options.Provider = Read("JEV__PROVIDER") ?? Read("JEV_PROVIDER");
        }

        var model = Read("JEV__MCP__MODEL") ?? Read("JEV_MCP_MODEL");
        if (!string.IsNullOrWhiteSpace(model))
        {
            options.Mcp.Model = model;
        }
        else if (string.IsNullOrWhiteSpace(options.Mcp.Model))
        {
            options.Mcp.Model = DefaultModel;
        }

        if (string.IsNullOrWhiteSpace(options.TypeSafeApiKey))
        {
            options.TypeSafeApiKey =
                Read("TYPESAFE__API__KEY") ?? Read("JEV_TYPESAFE_API_KEY") ?? Read("TYPESAFE_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(options.TypeSafeBaseUrl))
        {
            options.TypeSafeBaseUrl = Read("TYPESAFE__BASEURL") ?? Read("TYPESAFE_BASE_URL");
        }

        if (string.IsNullOrWhiteSpace(options.OpenRouterApiKey))
        {
            options.OpenRouterApiKey =
                Read("OPENROUTER__API__KEY") ?? Read("JEV_OPENROUTER_API_KEY") ?? Read("OPENROUTER_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(options.CloudflareApiToken))
        {
            options.CloudflareApiToken =
                Read("CLOUDFLARE__API__TOKEN")
                ?? Read("JEV_CLOUDFLARE_API_TOKEN")
                ?? Read("CLOUDFLARE_API_TOKEN");
        }

        if (string.IsNullOrWhiteSpace(options.CloudflareAccountId))
        {
            options.CloudflareAccountId =
                Read("CLOUDFLARE__ACCOUNT__ID")
                ?? Read("JEV_CLOUDFLARE_ACCOUNT_ID")
                ?? Read("CLOUDFLARE_ACCOUNT_ID");
        }

        if (string.IsNullOrWhiteSpace(options.AiGatewayApiKey))
        {
            options.AiGatewayApiKey =
                Read("AIGATEWAY__API__KEY") ?? Read("JEV_AI_GATEWAY_API_KEY") ?? Read("AI_GATEWAY_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(options.AiGatewayBaseUrl))
        {
            options.AiGatewayBaseUrl = Read("AIGATEWAY__BASEURL") ?? Read("AI_GATEWAY_BASE_URL");
        }

        if (string.IsNullOrWhiteSpace(options.CompatibleApiKey))
        {
            options.CompatibleApiKey = Read("COMPATIBLE__API__KEY") ?? Read("JEV_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(options.CompatibleBaseUrl))
        {
            options.CompatibleBaseUrl = Read("COMPATIBLE__BASEURL") ?? Read("JEV_API_BASE_URL");
        }
    }

    /// <summary>Reads configuration from the environment using the original variable names.</summary>
    public static JevProviderOptions FromEnvironment(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var options = new JevProviderOptions();
        FillNamedEnvironment(options, lookup);
        return options;
    }

    /// <summary>Reads configuration from the process environment variables.</summary>
    public static JevProviderOptions FromEnvironment()
    {
        return FromEnvironment(Environment.GetEnvironmentVariable);
    }

    /// <summary>Provider credential, for redaction in error messages.</summary>
    public string? SecretFor(JevProviderKind provider)
    {
        return provider switch
        {
            JevProviderKind.TypeSafe => TypeSafeApiKey,
            JevProviderKind.OpenRouter => OpenRouterApiKey,
            JevProviderKind.Cloudflare => CloudflareApiToken,
            JevProviderKind.Vercel => AiGatewayApiKey,
            JevProviderKind.Compatible => CompatibleApiKey,
            _ => null,
        };
    }

    /// <summary>Replaces any known credential. The audit log persists only already-redacted text.</summary>
    public string RedactSecrets(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var redacted = text;
        redacted = Secrets.Redact(redacted, TypeSafeApiKey);
        redacted = Secrets.Redact(redacted, OpenRouterApiKey);
        redacted = Secrets.Redact(redacted, CloudflareApiToken);
        redacted = Secrets.Redact(redacted, AiGatewayApiKey);
        redacted = Secrets.Redact(redacted, CompatibleApiKey);
        return redacted;
    }

    /// <summary>Redacts and truncates the error body, in the same order as the transport layer.</summary>
    public string SummarizeError(string text)
    {
        return Secrets.Summarize(RedactSecrets(text), secret: null);
    }
}

/// <summary>MCP model selection. Bound at <c>JEV:MCP:MODEL</c>.</summary>
public sealed class JevMcpOptions
{
    /// <summary>`JEV__MCP__MODEL` / <c>JEV:MCP:MODEL</c> (alias legado: `JEV_MCP_MODEL`).</summary>
    public string Model { get; set; } = JevProviderOptions.DefaultModel;
}

/// <summary>TypeSafe credentials. Bound at <c>TYPESAFE</c>, not under <c>JEV</c>.</summary>
public sealed class TypeSafeOptions
{
    public const string SectionName = "TYPESAFE";

    private TypeSafeApiOptions _api = new();

    /// <summary>`TYPESAFE:API` — pair of `TYPESAFE_API_KEY` / `TYPESAFE__API__KEY`.</summary>
    public TypeSafeApiOptions Api
    {
        get => _api;
        set => _api = value ?? new TypeSafeApiOptions();
    }

    /// <summary>`TYPESAFE_BASE_URL` / <c>TYPESAFE:BASEURL</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Copies bound TypeSafe values onto blank provider fields.</summary>
    public void CopyTo(JevProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.TypeSafeApiKey))
        {
            options.TypeSafeApiKey = Api.Key;
        }

        if (string.IsNullOrWhiteSpace(options.TypeSafeBaseUrl))
        {
            options.TypeSafeBaseUrl = BaseUrl;
        }
    }
}

/// <summary>TypeSafe API credential. Bound at <c>TYPESAFE:API:KEY</c>.</summary>
public sealed class TypeSafeApiOptions
{
    /// <summary>`TYPESAFE_API_KEY` / <c>TYPESAFE__API__KEY</c>.</summary>
    public string? Key { get; set; }
}

/// <summary>OpenRouter credentials. Bound at <c>OPENROUTER</c>, not under <c>JEV</c>.</summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OPENROUTER";

    private OpenRouterApiOptions _api = new();

    /// <summary>`OPENROUTER:API` — pair of `OPENROUTER_API_KEY` / `OPENROUTER__API__KEY`.</summary>
    public OpenRouterApiOptions Api
    {
        get => _api;
        set => _api = value ?? new OpenRouterApiOptions();
    }

    /// <summary>Copies bound OpenRouter values onto blank provider fields.</summary>
    public void CopyTo(JevProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.OpenRouterApiKey))
        {
            options.OpenRouterApiKey = Api.Key;
        }
    }
}

/// <summary>OpenRouter API credential. Bound at <c>OPENROUTER:API:KEY</c>.</summary>
public sealed class OpenRouterApiOptions
{
    /// <summary>`OPENROUTER_API_KEY` / <c>OPENROUTER__API__KEY</c>.</summary>
    public string? Key { get; set; }
}

/// <summary>Cloudflare Workers AI credentials. Bound at <c>CLOUDFLARE</c>, not under <c>JEV</c>.</summary>
public sealed class CloudflareOptions
{
    public const string SectionName = "CLOUDFLARE";

    private CloudflareApiOptions _api = new();
    private CloudflareAccountOptions _account = new();

    public CloudflareApiOptions Api
    {
        get => _api;
        set => _api = value ?? new CloudflareApiOptions();
    }

    public CloudflareAccountOptions Account
    {
        get => _account;
        set => _account = value ?? new CloudflareAccountOptions();
    }

    public void CopyTo(JevProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.CloudflareApiToken))
        {
            options.CloudflareApiToken = Api.Token;
        }

        if (string.IsNullOrWhiteSpace(options.CloudflareAccountId))
        {
            options.CloudflareAccountId = Account.Id;
        }
    }
}

/// <summary>Cloudflare API token. Bound at <c>CLOUDFLARE:API:TOKEN</c>.</summary>
public sealed class CloudflareApiOptions
{
    public string? Token { get; set; }
}

/// <summary>Cloudflare account id. Bound at <c>CLOUDFLARE:ACCOUNT:ID</c>.</summary>
public sealed class CloudflareAccountOptions
{
    public string? Id { get; set; }
}

/// <summary>Vercel AI Gateway credentials. Bound at <c>AIGATEWAY</c>, not under <c>JEV</c>.</summary>
public sealed class AiGatewayOptions
{
    public const string SectionName = "AIGATEWAY";

    private AiGatewayApiOptions _api = new();

    public AiGatewayApiOptions Api
    {
        get => _api;
        set => _api = value ?? new AiGatewayApiOptions();
    }

    public string? BaseUrl { get; set; }

    public void CopyTo(JevProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.AiGatewayApiKey))
        {
            options.AiGatewayApiKey = Api.Key;
        }

        if (string.IsNullOrWhiteSpace(options.AiGatewayBaseUrl))
        {
            options.AiGatewayBaseUrl = BaseUrl;
        }
    }
}

/// <summary>AI Gateway API key. Bound at <c>AIGATEWAY:API:KEY</c>.</summary>
public sealed class AiGatewayApiOptions
{
    public string? Key { get; set; }
}

/// <summary>Compatible endpoint credentials. Bound at <c>COMPATIBLE</c>, not under <c>JEV</c>.</summary>
public sealed class CompatibleOptions
{
    public const string SectionName = "COMPATIBLE";

    private CompatibleApiOptions _api = new();

    public CompatibleApiOptions Api
    {
        get => _api;
        set => _api = value ?? new CompatibleApiOptions();
    }

    public string? BaseUrl { get; set; }

    public void CopyTo(JevProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.CompatibleApiKey))
        {
            options.CompatibleApiKey = Api.Key;
        }

        if (string.IsNullOrWhiteSpace(options.CompatibleBaseUrl))
        {
            options.CompatibleBaseUrl = BaseUrl;
        }
    }
}

/// <summary>Compatible API key. Bound at <c>COMPATIBLE:API:KEY</c>.</summary>
public sealed class CompatibleApiOptions
{
    public string? Key { get; set; }
}
