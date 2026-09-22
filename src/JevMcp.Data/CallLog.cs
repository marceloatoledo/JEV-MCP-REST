namespace JevMcp.Data;

/// <summary>A tool call to the model, already reduced to what is worth persisting.</summary>
public sealed class CallLog
{
    public long Id { get; set; }

    /// <summary>Instant of the query, in UTC.</summary>
    public DateTime Instant { get; set; }

    public string Tool { get; set; } = "";

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public int DurationMs { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    /// <summary>Estimated USD at call time, using the tariff in effect when the row was written.</summary>
    public decimal CostUsd { get; set; }

    public string? ResultingAction { get; set; }

    public int QuestionCount { get; set; }

    public string Status { get; set; } = "";

    public string? Error { get; set; }

    public string? RequestPayload { get; set; }

    public string? ResponsePayload { get; set; }

    public string? TokenName { get; set; }

    public string? TokenPrefix { get; set; }

    /// <summary>Id of the <see cref="McpAccessToken"/> used for the call, when authenticated with a bearer token.</summary>
    public long? AccessTokenId { get; set; }
}
