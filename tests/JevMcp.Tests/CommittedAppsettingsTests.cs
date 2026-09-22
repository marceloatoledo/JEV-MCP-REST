using System.Text.Json;

namespace JevMcp.Tests;

/// <summary>
/// Docker and production fill credentials from the environment. Committed JSON
/// must stay empty so those variables are not shadowed.
/// </summary>
public sealed class CommittedAppsettingsTests
{
    private static readonly string[] SecretPaths =
    [
        "ACCESSCONTROL:USERNAME",
        "ACCESSCONTROL:PASSWORD",
        "TYPESAFE:API:KEY",
        "OPENROUTER:API:KEY",
        "CLOUDFLARE:API:TOKEN",
        "CLOUDFLARE:ACCOUNT:ID",
        "AIGATEWAY:API:KEY",
        "COMPATIBLE:API:KEY",
        "COMPATIBLE:BASEURL",
    ];

    [Fact]
    public void ProductionAppsettingsOmitsProviderAndAccessSections()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "JevMcp.App", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Null(Read(document.RootElement, "ACCESSCONTROL:USERNAME"));
        Assert.Null(Read(document.RootElement, "TYPESAFE:API:KEY"));
        Assert.Equal("data/jevmcp.db", Read(document.RootElement, "CALLAUDIT:DATABASEPATH"));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void CommittedAppsettingsLeaveRuntimeSecretsEmpty(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "JevMcp.App", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var secret in SecretPaths)
        {
            var value = Read(document.RootElement, secret);
            if (value is null)
            {
                continue;
            }

            Assert.True(
                string.IsNullOrWhiteSpace(value),
                $"{fileName} must leave {secret} empty so Docker/production environment variables apply.");
        }
    }

    [Fact]
    public void DevelopmentAppsettingsUsesUppercaseKeysAndNestsTheMcpModelAtThreeLevels()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "JevMcp.App", "appsettings.Development.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal("auto", Read(document.RootElement, "JEV:PROVIDER"));
        Assert.Equal("jev-latest", Read(document.RootElement, "JEV:MCP:MODEL"));
        Assert.Null(Read(document.RootElement, "JEV:MODEL"));
        Assert.Equal("", Read(document.RootElement, "TYPESAFE:API:KEY"));
        Assert.Null(Read(document.RootElement, "JEV:TypeSafeApiKey"));
        Assert.Equal("", Read(document.RootElement, "OPENROUTER:API:KEY"));
        Assert.Equal("", Read(document.RootElement, "CLOUDFLARE:API:TOKEN"));
        Assert.Equal("", Read(document.RootElement, "CLOUDFLARE:ACCOUNT:ID"));
        Assert.Equal("", Read(document.RootElement, "AIGATEWAY:API:KEY"));
        Assert.Equal("", Read(document.RootElement, "COMPATIBLE:API:KEY"));
        Assert.Equal("", Read(document.RootElement, "COMPATIBLE:BASEURL"));
    }

    private static string? Read(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split(':'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JevMcp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("JevMcp.slnx not found above the test output directory.");
    }
}
