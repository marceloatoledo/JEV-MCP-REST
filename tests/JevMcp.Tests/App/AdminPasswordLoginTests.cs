using JevMcp.App;
using JevMcp.Data;

namespace JevMcp.Tests;

public sealed class AdminPasswordLoginTests
{
    [Fact]
    public void MatchesTheConfiguredPair()
    {
        var options = new AccessControlOptions { Username = "ada", Password = "secret" };
        Assert.True(AdminPasswordLogin.Matches(options, "ada", "secret"));
    }

    [Fact]
    public void RejectsTheWrongPasswordWithoutDistinguishingTheField()
    {
        var options = new AccessControlOptions { Username = "ada", Password = "secret" };
        Assert.False(AdminPasswordLogin.Matches(options, "ada", "other"));
        Assert.False(AdminPasswordLogin.Matches(options, "other", "secret"));
    }

    [Fact]
    public void EmptyConfigurationNeverMatchesIncludingEmptyInput()
    {
        var options = new AccessControlOptions();
        Assert.False(options.HasAdminPassword);
        Assert.False(AdminPasswordLogin.Matches(options, "", ""));
        Assert.False(AdminPasswordLogin.Matches(options, "ada", "secret"));
    }

    [Fact]
    public void FillNamedEnvironmentDoesNotOverrideBoundValues()
    {
        var options = new AccessControlOptions { Username = "bound", Password = "bound-secret" };
        AccessControlOptions.FillNamedEnvironment(options, _ => "from-env");
        Assert.Equal("bound", options.Username);
        Assert.Equal("bound-secret", options.Password);
    }

    [Fact]
    public void FillNamedEnvironmentReadsTheNamedVariablesWhenBlank()
    {
        var options = new AccessControlOptions();
        AccessControlOptions.FillNamedEnvironment(options, name => name switch
        {
            AccessControlOptions.LegacyUsernameVariable => "env-user",
            AccessControlOptions.LegacyPasswordVariable => "env-pass",
            _ => null,
        });

        Assert.Equal("env-user", options.Username);
        Assert.Equal("env-pass", options.Password);
        Assert.True(options.HasAdminPassword);
    }
}
