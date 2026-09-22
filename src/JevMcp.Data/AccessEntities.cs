namespace JevMcp.Data;

/// <summary>
/// Access token for <c>/mcp</c> and the admin. The cleartext value exists only at
/// creation; after that the database stores the hash and a visible prefix.
/// </summary>
public sealed class McpAccessToken
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public string Hash { get; set; } = "";

    public string Prefix { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public bool Active { get; set; } = true;
}

/// <summary>The full value, returned once at creation.</summary>
public sealed record IssuedAccessToken(McpAccessToken Record, string Secret);

/// <summary>Persisted operational option, so it can change without restarting the process.</summary>
public sealed class AppSetting
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
