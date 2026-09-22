namespace JevMcp.Providers;

/// <summary>Credential redaction in text that may reach the MCP client or the UI.</summary>
internal static class Secrets
{
    /// <summary>Error body limit: enough to diagnose without dumping proxy HTML into the log.</summary>
    public const int MaxErrorBodyChars = 200;

    public const string Placeholder = "[redacted]";

    /// <summary>
    /// Replaces every occurrence of the credential. Covering the bare form also covers
    /// the `Bearer &lt;credential&gt;` form, which is how an endpoint that echoes the
    /// request would return the key.
    /// </summary>
    public static string Redact(string text, string? secret)
    {
        return string.IsNullOrEmpty(secret) ? text : text.Replace(secret, Placeholder, StringComparison.Ordinal);
    }

    /// <summary>Redacts then truncates, in that order: truncating first would leave half the key visible.</summary>
    public static string Summarize(string text, string? secret)
    {
        var redacted = Redact(text, secret);
        return redacted.Length <= MaxErrorBodyChars ? redacted : redacted[..MaxErrorBodyChars];
    }
}
