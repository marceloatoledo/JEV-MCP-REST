namespace JevMcp.Core;

/// <summary>
/// Request limits inherited from jev-mcp. Applied before any model call.
/// </summary>
public static class Limits
{
    /// <summary>Candidates per find call. TypeSafe Choice supports up to 255 options.</summary>
    public const int MaxCandidates = 250;

    /// <summary>Text cap per candidate, to keep the request size bounded.</summary>
    public const int MaxCandidateChars = 2_000;

    /// <summary>
    /// Tolerance on the probability sum. The epsilon exists because a mathematically
    /// exact 0.01 difference can compare as greater than 0.01 in IEEE-754.
    /// </summary>
    public const double ProbabilitySumTolerance = 0.01 + 1e-12;

    /// <summary>Slack for accepting a tie at the highest probability.</summary>
    public const double MaximumProbabilityTolerance = 1e-9;

    /// <summary>Classes per classify call, limited by Choice option count.</summary>
    public const int MaxClasses = 250;

    /// <summary>Items per classify call; each becomes an independent question.</summary>
    public const int MaxItems = 64;

    /// <summary>Text cap per classified item.</summary>
    public const int MaxItemChars = 2_000;

    /// <summary>Item-class pair budget per classify batch.</summary>
    public const int MaxClassifyItemClassBudget = 8_000;

    /// <summary>Candidates per decide call.</summary>
    public const int MaxDecideCandidates = 6;

    /// <summary>Requirements checked per decide call.</summary>
    public const int MaxRequirements = 3;

    /// <summary>Candidates per rerank call.</summary>
    public const int MaxRerankCandidates = 250;

    /// <summary>Aggregate text budget of candidates in a rerank call.</summary>
    public const int MaxRerankTotalChars = 100_000;

    /// <summary>Aspects judged independently in a comparison.</summary>
    public const int MaxCompareAspects = 10;

    /// <summary>Cap per compared passage; above this the request is rejected.</summary>
    public const int MaxComparePassageChars = 20_000;

    /// <summary>Fields per extract call.</summary>
    public const int MaxExtractFields = 32;

    /// <summary>Regex matches sent per field before the set is marked truncated.</summary>
    public const int MaxExtractCandidates = 20;

    /// <summary>A match longer than this is dropped and flagged, never silently truncated.</summary>
    public const int MaxExtractCandidateChars = 2_000;

    /// <summary>Aggregate budget of matches sent in an extract call.</summary>
    public const int MaxExtractTotalChars = 50_000;

    /// <summary>Document submitted to extract.</summary>
    public const int MaxExtractDocumentChars = 50_000;

    /// <summary>Timeout for the caller-supplied regex.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Completion claims per gate call; each adds a question.</summary>
    public const int MaxGateClaims = 16;

    /// <summary>Evidence items per gate call.</summary>
    public const int MaxGateEvidenceItems = 16;

    /// <summary>Aggregate evidence budget per gate call, before per-item truncation.</summary>
    public const int MaxGateEvidenceChars = 200_000;

    /// <summary>Cap per request, diff, tests, and evidence document.</summary>
    public const int MaxReviewDocumentChars = 50_000;

    /// <summary>Cap per claim; claims are bounded assertions.</summary>
    public const int MaxClaimChars = 2_000;
}
