using JevMcp.Providers;

namespace JevMcp.Data;

/// <summary>Redaction and truncation applied before any text goes to the database.</summary>
internal static class AuditText
{
    public const string TruncatedMark = "\n[truncated]";

    public const string StatusOk = "ok";

    public const string StatusError = "error";

    public static string? Capture(string? value, CallAuditOptions options, JevProviderOptions secrets)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!options.CapturePayloads || string.IsNullOrEmpty(value))
        {
            return null;
        }

        return Truncate(secrets.RedactSecrets(value), options.PayloadMaxChars);
    }

    public static string Truncate(string value, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (maxChars < TruncatedMark.Length || value.Length <= maxChars)
        {
            return value.Length <= maxChars ? value : TruncatedMark.TrimStart();
        }

        return value[..(maxChars - TruncatedMark.Length)] + TruncatedMark;
    }
}
