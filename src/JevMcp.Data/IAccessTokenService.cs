namespace JevMcp.Data;

/// <summary>Issuance, verification, and revocation of access tokens.</summary>
public interface IAccessTokenService
{
    Task<IReadOnlyList<McpAccessToken>> ListAsync(CancellationToken cancellationToken = default);

    Task<IssuedAccessToken> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<McpAccessToken?> AuthenticateAsync(string? secret, CancellationToken cancellationToken = default);

    Task RevokeAsync(long id, CancellationToken cancellationToken = default);

    Task ReactivateAsync(long id, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> HasActiveAsync(CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(long id, CancellationToken cancellationToken = default);

    void Touch(long id);
}

/// <summary>Options the admin can change without restarting the process.</summary>
public interface IOperationalSettings
{
    bool AllowAnonymousMcp { get; }

    bool CapturePayloads { get; }

    int RetentionDays { get; }

    /// <summary>OS or IANA time zone id for UI timestamps; <see cref="DisplaySettings.DefaultTimeZoneId"/> means UTC.</summary>
    string DisplayTimeZoneId { get; }

    IReadOnlyDictionary<string, decimal> UsdPerMillionByProvider { get; }

    decimal GetUsdPerMillionTokens(string? provider);

    event Action? Changed;

    Task EnsureDefaultsAsync(
        bool allowAnonymousMcp,
        bool capturePayloads,
        int retentionDays,
        CancellationToken cancellationToken = default);

    Task SetAllowAnonymousMcpAsync(bool value, CancellationToken cancellationToken = default);

    Task SetCapturePayloadsAsync(bool value, CancellationToken cancellationToken = default);

    Task SetRetentionDaysAsync(int value, CancellationToken cancellationToken = default);

    Task SetDisplayTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default);

    Task SetUsdPerMillionTokensAsync(
        IReadOnlyDictionary<string, decimal> ratesByProvider,
        CancellationToken cancellationToken = default);

    Task UpsertUsdPerMillionTokensAsync(
        string provider,
        decimal rate,
        CancellationToken cancellationToken = default);

    Task DeleteUsdPerMillionTokensAsync(string provider, CancellationToken cancellationToken = default);
}
