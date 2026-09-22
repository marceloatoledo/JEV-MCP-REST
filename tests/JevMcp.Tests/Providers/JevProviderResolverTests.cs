using JevMcp.App;
using JevMcp.Providers;
using Microsoft.Extensions.Configuration;

namespace JevMcp.Tests;

public sealed class JevProviderResolverTests
{
    [Fact]
    public void TypeSafeWinsOverOpenRouterInAutomaticDetection()
    {
        var resolution = JevProviderResolver.Resolve(new JevProviderOptions
        {
            TypeSafeApiKey = "ts-key",
            OpenRouterApiKey = "sk-or-key",
        });

        Assert.Equal(JevProviderKind.TypeSafe, resolution.Provider);
    }

    [Fact]
    public void AutomaticPrecedenceFollowsTheDocumentedOrder()
    {
        var all = new JevProviderOptions
        {
            TypeSafeApiKey = "ts-key",
            OpenRouterApiKey = "sk-or-key",
            CloudflareApiToken = "cf-token",
            CloudflareAccountId = "cf-account",
            AiGatewayApiKey = "gw-key",
            CompatibleApiKey = "jev-key",
            CompatibleBaseUrl = "https://example.test/v1/systemone",
        };

        Assert.Equal(JevProviderKind.TypeSafe, JevProviderResolver.Resolve(all).Provider);

        all.TypeSafeApiKey = null;
        Assert.Equal(JevProviderKind.OpenRouter, JevProviderResolver.Resolve(all).Provider);

        all.OpenRouterApiKey = null;
        Assert.Equal(JevProviderKind.Cloudflare, JevProviderResolver.Resolve(all).Provider);

        all.CloudflareApiToken = null;
        Assert.Equal(JevProviderKind.Vercel, JevProviderResolver.Resolve(all).Provider);

        all.AiGatewayApiKey = null;
        Assert.Equal(JevProviderKind.Compatible, JevProviderResolver.Resolve(all).Provider);
    }

    [Fact]
    public void OpenRouterKeyWithoutPrefixDoesNotCount()
    {
        var resolution = JevProviderResolver.Resolve(new JevProviderOptions { OpenRouterApiKey = "key-sem-prefixo" });

        Assert.Null(resolution.Provider);
    }

    [Fact]
    public void CloudflareRequiresTokenAndAccount()
    {
        Assert.Null(JevProviderResolver.Resolve(new JevProviderOptions { CloudflareApiToken = "cf" }).Provider);
        Assert.Null(JevProviderResolver.Resolve(new JevProviderOptions { CloudflareAccountId = "acc" }).Provider);
    }

    [Fact]
    public void CloudflareSpecificTokenTakesPrecedenceOverTheGenericOne()
    {
        var options = JevProviderOptions.FromEnvironment(name => name switch
        {
            "JEV_CLOUDFLARE_API_TOKEN" => "especifico",
            "CLOUDFLARE_API_TOKEN" => "generico",
            "CLOUDFLARE_ACCOUNT_ID" => "conta",
            _ => null,
        });

        Assert.Equal("especifico", options.CloudflareApiToken);
        Assert.Equal(JevProviderKind.Cloudflare, JevProviderResolver.Resolve(options).Provider);
    }

    [Theory]
    [InlineData("typesafe", "TYPESAFE_API_KEY")]
    [InlineData("openrouter", "OPENROUTER_API_KEY")]
    [InlineData("vercel", "AI_GATEWAY_API_KEY")]
    [InlineData("cloudflare", "CLOUDFLARE_ACCOUNT_ID")]
    public void ExplicitChoiceWithoutCredentialNamesTheMissingVariable(string provider, string variable)
    {
        var resolution = JevProviderResolver.Resolve(new JevProviderOptions { Provider = provider });

        Assert.Null(resolution.Provider);
        Assert.Contains(variable, resolution.Error!, StringComparison.Ordinal);
        Assert.Contains($"JEV_PROVIDER={provider}", resolution.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibleListsBothVariablesInPluralAndOneInSingular()
    {
        var both = JevProviderResolver.Resolve(new JevProviderOptions { Provider = "compatible" });
        Assert.Contains("JEV_API_KEY and JEV_API_BASE_URL are not set.", both.Error!, StringComparison.Ordinal);

        var onlyUrl = JevProviderResolver.Resolve(new JevProviderOptions
        {
            Provider = "compatible",
            CompatibleApiKey = "jev-key",
        });
        Assert.Contains("JEV_API_BASE_URL is not set.", onlyUrl.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitChoiceTakesPrecedenceOverAnotherProvidersCredential()
    {
        var resolution = JevProviderResolver.Resolve(new JevProviderOptions
        {
            Provider = "vercel",
            TypeSafeApiKey = "ts-key",
            AiGatewayApiKey = "gw-key",
        });

        Assert.Equal(JevProviderKind.Vercel, resolution.Provider);
    }

    [Fact]
    public void UnknownProviderNameIsAnErrorAndDoesNotFallBackToDetection()
    {
        var resolution = JevProviderResolver.Resolve(new JevProviderOptions
        {
            Provider = "typesafeai",
            TypeSafeApiKey = "ts-key",
        });

        Assert.Null(resolution.Provider);
        Assert.Contains("is not a known provider", resolution.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCredentialsMakesResolutionFailListingTheOptions()
    {
        var resolution = JevProviderResolver.Resolve(new JevProviderOptions());

        Assert.False(resolution.IsConfigured);
        Assert.Contains("No Jev provider credentials found", resolution.Error!, StringComparison.Ordinal);
        Assert.Throws<JevConfigurationException>(() => resolution.Require());
    }

    [Fact]
    public void DefaultModelIsJevLatest()
    {
        Assert.Equal("jev-latest", JevProviderOptions.FromEnvironment(_ => null).Model);
        Assert.Equal("jev-1.12", JevProviderOptions.FromEnvironment(name =>
            name == "JEV_MCP_MODEL" ? "jev-1.12" : null).Model);
        Assert.Equal("jev-1.12", JevProviderOptions.FromEnvironment(name =>
            name == "JEV__MCP__MODEL" ? "jev-1.12" : null).Model);
    }

    [Fact]
    public void HierarchicalEnvironmentVariablesFillProvider()
    {
        var options = new JevProviderOptions { Provider = "" };
        JevProviderOptions.FillNamedEnvironment(options, name =>
            name == "JEV__PROVIDER" ? "typesafe" : null);

        Assert.Equal("typesafe", options.Provider);
    }

    [Fact]
    public void NamedEnvironmentFillsBlankBoundProviderAndKey()
    {
        var options = new JevProviderOptions { Provider = "", TypeSafeApiKey = "" };
        JevProviderOptions.FillNamedEnvironment(options, name => name switch
        {
            "JEV_PROVIDER" => "typesafe",
            "TYPESAFE_API_KEY" => "from-env",
            _ => null,
        });

        Assert.Equal("typesafe", options.Provider);
        Assert.Equal("from-env", options.TypeSafeApiKey);
    }

    [Fact]
    public void HierarchicalEnvironmentFillsBlankProviderCredentials()
    {
        var options = new JevProviderOptions { TypeSafeApiKey = "", OpenRouterApiKey = "" };
        JevProviderOptions.FillNamedEnvironment(options, name => name switch
        {
            "TYPESAFE__API__KEY" => "ts-hier",
            "OPENROUTER__API__KEY" => "or-hier",
            _ => null,
        });

        Assert.Equal("ts-hier", options.TypeSafeApiKey);
        Assert.Equal("or-hier", options.OpenRouterApiKey);
    }

    [Fact]
    public void BindReadsJevProviderMcpModelAtThreeLevels()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JEV:MCP:MODEL"] = "jev-1.12",
            })
            .Build();

        var options = new JevProviderOptions();
        configuration.GetSection(JevProviderOptions.SectionName).Bind(options);

        Assert.Equal("jev-1.12", options.Mcp.Model);
        Assert.Equal("jev-1.12", options.Model);
    }

    [Fact]
    public void BindReadsTypeSafeApiKeyAtThreeLevels()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TYPESAFE:API:KEY"] = "from-json",
                ["TYPESAFE:BASEURL"] = "https://interno.example.test",
            })
            .Build();

        var options = JevProviderConfiguration.Load(configuration);

        Assert.Equal("from-json", options.TypeSafeApiKey);
        Assert.Equal("https://interno.example.test", options.TypeSafeBaseUrl);
    }

    [Fact]
    public void BindReadsOpenRouterApiKeyAtThreeLevels()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:Api:Key"] = "sk-or-from-json",
            })
            .Build();

        var options = JevProviderConfiguration.Load(configuration);

        Assert.Equal("sk-or-from-json", options.OpenRouterApiKey);
    }

    [Fact]
    public void BindReadsCloudflareCredentialsAtThreeLevels()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudflare:Api:Token"] = "cf-token",
                ["Cloudflare:Account:Id"] = "account-123",
            })
            .Build();

        var options = JevProviderConfiguration.Load(configuration);

        Assert.Equal("cf-token", options.CloudflareApiToken);
        Assert.Equal("account-123", options.CloudflareAccountId);
    }

    [Fact]
    public void BindReadsAiGatewayAndCompatibleAtThreeLevels()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiGateway:Api:Key"] = "gw-key",
                ["AiGateway:BaseUrl"] = "https://gateway.example.test/v4/ai",
                ["Compatible:Api:Key"] = "compat-key",
                ["Compatible:BaseUrl"] = "https://compat.example.test/v1/decide",
            })
            .Build();

        var options = JevProviderConfiguration.Load(configuration);

        Assert.Equal("gw-key", options.AiGatewayApiKey);
        Assert.Equal("https://gateway.example.test/v4/ai", options.AiGatewayBaseUrl);
        Assert.Equal("compat-key", options.CompatibleApiKey);
        Assert.Equal("https://compat.example.test/v1/decide", options.CompatibleBaseUrl);
    }
}
