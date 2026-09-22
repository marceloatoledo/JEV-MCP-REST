namespace JevMcp.Data;

/// <summary>Protection of <c>/mcp</c>, <c>/api/jev</c>, and the admin login pair.</summary>
public sealed class AccessControlOptions
{
    public const string SectionName = "ACCESSCONTROL";

    public const string AllowAnonymousKey = "allow_anonymous_mcp";

    /// <summary>Allows calling <c>/mcp</c> and <c>/api/jev</c> without Bearer. Off by default, and noisy when on.</summary>
    public bool AllowAnonymousMcp { get; set; }

    public const string UsernameEnvironmentKey = "ACCESSCONTROL__USERNAME";

    public const string PasswordEnvironmentKey = "ACCESSCONTROL__PASSWORD";

    public const string LegacyUsernameVariable = "JEVMCP_ADMIN_USERNAME";

    public const string LegacyPasswordVariable = "JEVMCP_ADMIN_PASSWORD";

    public const string ObsoleteLegacyUsernameVariable = "JAVMCP_ADMIN_USERNAME";

    public const string ObsoleteLegacyPasswordVariable = "JAVMCP_ADMIN_PASSWORD";

    /// <summary>Admin login name. Prefer the same-named environment variable.</summary>
    public string? Username { get; set; }

    /// <summary>Admin login password. Prefer the same-named environment variable. Never log this.</summary>
    public string? Password { get; set; }

    public bool HasAdminPassword =>
        !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);

    /// <summary>
    /// Fills blank fields from named environment variables. Bound configuration
    /// (appsettings, <c>ACCESSCONTROL__*</c>) wins when already set.
    /// </summary>
    public static void FillNamedEnvironment(AccessControlOptions options, Func<string, string?> getenv)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(getenv);

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            options.Username = getenv(LegacyUsernameVariable) ?? getenv(ObsoleteLegacyUsernameVariable);
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            options.Password = getenv(LegacyPasswordVariable) ?? getenv(ObsoleteLegacyPasswordVariable);
        }
    }
}
