namespace JevMcp.Data;

internal static class CallAuditOptionsOverlay
{
    public static CallAuditOptions Effective(CallAuditOptions configured, IOperationalSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(configured);

        if (settings is null)
        {
            return configured;
        }

        return new CallAuditOptions
        {
            DatabasePath = configured.DatabasePath,
            CapturePayloads = settings.CapturePayloads,
            PayloadMaxChars = configured.PayloadMaxChars,
            RetentionDays = settings.RetentionDays,
        };
    }
}
