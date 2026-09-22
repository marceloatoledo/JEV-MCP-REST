namespace JevMcp.App;

/// <summary>First-time setup state. Only the host writes; pages read.</summary>
public sealed class AccessControlState
{
    public bool SetupRequired { get; set; }
}

internal static class AccessClaims
{
    public const string TokenId = "mcp_token_id";

    public const string TokenName = "mcp_token_name";

    public const string TokenPrefix = "mcp_token_prefix";
}

internal static class Loopback
{
    public static bool IsLocal(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remote = context.Connection.RemoteIpAddress;
        if (remote is null)
        {
            return true;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        return System.Net.IPAddress.IsLoopback(remote);
    }

    public static bool IsExposedBinding(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return false;
        }

        foreach (var part in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('+', StringComparison.Ordinal) ||
                part.Contains('*', StringComparison.Ordinal) ||
                part.Contains("0.0.0.0", StringComparison.Ordinal) ||
                part.Contains("[::]", StringComparison.Ordinal))
            {
                return true;
            }

            if (Uri.TryCreate(part, UriKind.Absolute, out var uri) &&
                !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) &&
                !string.Equals(uri.Host, "::1", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
