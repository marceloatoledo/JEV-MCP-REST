using Microsoft.EntityFrameworkCore;

namespace JevMcp.Data;

internal sealed class AccessTokenService : IAccessTokenService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IAccessTokenTouchSink _touch;

    public AccessTokenService(IDbContextFactory<AppDbContext> factory, IAccessTokenTouchSink touch)
    {
        _factory = factory;
        _touch = touch;
    }

    public async Task<IReadOnlyList<McpAccessToken>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.AccessTokens
            .AsNoTracking()
            .OrderBy(token => token.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IssuedAccessToken> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = RequireName(name);
        var secret = AccessTokenFormat.Generate();
        var record = await InsertAsync(trimmed, secret, cancellationToken).ConfigureAwait(false);
        return new IssuedAccessToken(record, secret);
    }

    public async Task<McpAccessToken?> AuthenticateAsync(string? secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        var presented = AccessTokenFormat.Hash(secret);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await db.AccessTokens
            .AsNoTracking()
            .Where(token => token.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        McpAccessToken? match = null;
        foreach (var candidate in candidates)
        {
            if (AccessTokenFormat.FixedEqualsHex(candidate.Hash, presented))
            {
                match = candidate;
            }
        }

        if (match is not null)
        {
            _touch.Touch(match.Id);
        }

        return match;
    }

    public Task RevokeAsync(long id, CancellationToken cancellationToken = default)
    {
        return SetActiveAsync(id, active: false, cancellationToken);
    }

    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        return SetActiveAsync(id, active: true, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.AccessTokens.FirstOrDefaultAsync(token => token.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Access token {id} was not found.");

        db.AccessTokens.Remove(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.AccessTokens.AnyAsync(token => token.Active, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsActiveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.AccessTokens.AnyAsync(token => token.Id == id && token.Active, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Touch(long id) => _touch.Touch(id);

    private async Task<McpAccessToken> InsertAsync(string name, string secret, CancellationToken cancellationToken)
    {
        var record = new McpAccessToken
        {
            Name = name,
            Hash = AccessTokenFormat.HashHex(secret),
            Prefix = AccessTokenFormat.VisiblePrefix(secret),
            CreatedAt = DateTime.UtcNow,
            Active = true,
        };

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.AccessTokens.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    private async Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.AccessTokens.FirstOrDefaultAsync(token => token.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Access token {id} was not found.");

        record.Active = active;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string RequireName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > 64)
        {
            throw new ArgumentException("Token name must be 64 characters or fewer.", nameof(name));
        }

        return trimmed;
    }
}
