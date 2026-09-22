using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace JevMcp.Tools.Mcp;

/// <summary>MCP shell of <see cref="ReviewService"/>.</summary>
[McpServerToolType]
public sealed class JevReviewTool
{
    private readonly ReviewService _service;

    public JevReviewTool(ReviewService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Review, Title = "Review a proposed patch", ReadOnly = true)]
    [Description(
        "Score a proposed diff against the request with TypeSafe Jev before the task is called done. " +
        "Returns 0..2 rubric scores for correctness, spec match, test gap, and blast radius (the last two lower " +
        "the weighted composite), a safe_to_apply probability, and an auto | review | escalate action. Auto " +
        "requires safe_to_apply and min score confidence at auto_accept and the composite at composite_floor; " +
        "truncated or malformed input never returns auto. Does not apply the patch or run tests. " +
        "Use jev_gate to also verify completion claims against evidence in the same call.")]
    public async Task<string> ReviewAsync(
        [Description("What the user asked for; this frames the review, it is not proof of anything.")]
        string request,
        [Description("Proposed patch, file excerpt, or change summary. Truncated at 50,000 chars.")]
        string diff,
        [Description("Reported test output, if any. Truncated at the same cap.")]
        string? tests = null,
        [Description(
            "safe_to_apply and min score confidence at or above this may stand automatically. Default 0.8.")]
        double? auto_accept = null,
        [Description(
            "Min score confidence or safe_to_apply below this escalates. Must be <= auto_accept. " +
            "Default min(0.5, auto_accept).")]
        double? review_at = null,
        [Description("Weighted composite at or above this is required for auto. Default 0.7.")]
        double? composite_floor = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new ReviewRequest(request, diff, tests, auto_accept, review_at, composite_floor);

        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(payload, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}

/// <summary>MCP shell of <see cref="GateService"/>.</summary>
[McpServerToolType]
public sealed class JevGateTool
{
    private readonly GateService _service;

    public JevGateTool(GateService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Gate, Title = "Gate completion: review a patch and verify claims", ReadOnly = true)]
    [Description(
        "Review a proposed patch and verify completion claims against supplied evidence in one TypeSafe Jev call. " +
        "Auto only when the patch review is accepted and every claim is verified at or above auto_accept. " +
        "Unsupported claims require review; confident contradictions, unknown confidence, or low confidence " +
        "escalate. The request and claims are assertions to check, never proof; put supporting diff excerpts and " +
        "test logs in evidence. Evidence is capped at 16 items and 200,000 characters in aggregate. " +
        "Does not run tests or apply changes. Use jev_review for a patch without claims, jev_verify for " +
        "claims without a patch review.")]
    public async Task<string> GateAsync(
        [Description("What the user asked for; this is not evidence of completion.")]
        string request,
        [Description("Proposed patch, file excerpt, or change summary. Truncated at 50,000 chars.")]
        string diff,
        [Description(
            "Completion claims to check against evidence, each truncated at 2,000 chars. Up to 16 per call.")]
        string[] claims,
        [Description(
            "Evidence the claims are checked against: a single document as text, one item with an optional id " +
            "and a text field, or an array of such items. At least one item must have non-empty text.")]
        JsonElement evidence,
        [Description("Reported test output for the patch review. Truncated at the same cap.")]
        string? tests = null,
        [Description("Review and per-claim confidence at or above this may stand automatically. Default 0.8.")]
        double? auto_accept = null,
        [Description(
            "Score, safe_to_apply, or per-claim confidence below this escalates. Must be <= auto_accept. " +
            "Default min(0.5, auto_accept).")]
        double? review_at = null,
        [Description("Weighted composite at or above this is required for auto. Default 0.7.")]
        double? composite_floor = null,
        CancellationToken cancellationToken = default)
    {
        var payload = Invalid.Guard(() => new GateRequest(
            request,
            diff,
            claims,
            EvidenceInput.Parse(evidence),
            tests,
            auto_accept,
            review_at,
            composite_floor));

        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(payload, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}
