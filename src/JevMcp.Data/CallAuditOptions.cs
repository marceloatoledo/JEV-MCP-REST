namespace JevMcp.Data;

/// <summary>Persistence and retention of call history.</summary>
public sealed class CallAuditOptions
{
    public const string SectionName = "CALLAUDIT";

    public const string CapturePayloadsKey = "capture_payloads";

    public const string RetentionDaysKey = "retention_days";

    /// <summary>Path of the SQLite file. Relative to the content root when it is not absolute.</summary>
    public string DatabasePath { get; set; } = "data/jevmcp.db";

    /// <summary>Stores request and response payloads. Off by default: the caller document must not become a copy in the database.</summary>
    public bool CapturePayloads { get; set; }

    /// <summary>Cap on persisted text when capture is on.</summary>
    public int PayloadMaxChars { get; set; } = 4_096;

    /// <summary>Records older than this are removed. Zero or negative disables purge.</summary>
    public int RetentionDays { get; set; } = 30;
}
