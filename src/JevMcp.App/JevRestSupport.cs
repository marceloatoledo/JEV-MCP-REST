using System.Text.Json;
using JevMcp.Data;
using JevMcp.Providers;
using JevMcp.Tools;

namespace JevMcp.App;

internal static class JevRestCatalog
{
    public static readonly JevRestToolInfo[] Tools =
    [
        new(JevTools.Verify, "POST", "/api/jev/verify", "Verify claims against evidence",
            "Check each claim against provided evidence text with TypeSafe Jev. Returns per claim: " +
            "verdict (verified | contradicted | unsupported), full probability distribution, confidence, " +
            "and whether the verdict stands on its own (auto) or needs human review."),
        new(JevTools.Screen, "POST", "/api/jev/screen", "Screen content before it enters agent context",
            "Judge fetched or external text with TypeSafe Jev before an agent reads it: probability it contains " +
            "instructions aimed at an AI agent (prompt injection), whether it has substantive content, and (when a purpose " +
            "is given) whether it is relevant to the task. Returns a recommendation: pass | review | block | skip."),
        new(JevTools.Find, "POST", "/api/jev/find", "Semantic search over candidates",
            "Rank candidates against a plain-language query with TypeSafe Jev — no embeddings needed."),
        new(JevTools.Classify, "POST", "/api/jev/classify", "Classify items against a shared label set",
            "Assign each item to one class from a shared catalog with TypeSafe Jev, in one batched request."),
        new(JevTools.Decide, "POST", "/api/jev/decide", "Decide between bounded alternatives",
            "One unresolved, bounded decision where semantic judgment over supplied evidence could change your plan."),
        new(JevTools.Rerank, "POST", "/api/jev/rerank", "Score every candidate's relevance and return them sorted",
            "Rerank candidates against a query with TypeSafe Jev: one independent relevance probability per candidate."),
        new(JevTools.Compare, "POST", "/api/jev/compare", "Compare two passages for factual agreement",
            "Judge the relation between two passages with TypeSafe Jev: same_fact, contradicts, or different_facts."),
        new(JevTools.Extract, "POST", "/api/jev/extract", "Extract fields by regex, Jev picks the right match",
            "Extract structured fields from a document with TypeSafe Jev as the picker, not the generator."),
        new(JevTools.Review, "POST", "/api/jev/review", "Review a proposed patch",
            "Score a proposed diff against the request with TypeSafe Jev before the task is called done."),
        new(JevTools.Gate, "POST", "/api/jev/gate", "Gate completion: review a patch and verify claims",
            "Review a proposed patch and verify completion claims against supplied evidence in one TypeSafe Jev call."),
    ];

    public static JevRestToolInfo Of(string tool) =>
        Tools.First(item => string.Equals(item.Tool, tool, StringComparison.Ordinal));
}

internal static class JevRest
{
    public static async Task<IResult> RunAsync<T>(
        HttpContext http,
        ICallAudit audit,
        string tool,
        object request,
        Func<Task<T>> run)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(run);

        using var scope = audit.Begin(tool);
        CallAuditOrigin.Apply(scope, http.User);
        scope.SetRequestPayload(JsonSerializer.Serialize(request, JevJson.Options));

        try
        {
            var result = await run().ConfigureAwait(false);
            var json = JevJson.Serialize(result);
            scope.Complete(json, isError: PlaygroundOutcome.FromJson(json).Failed);
            return Results.Content(json, "application/json");
        }
        catch (Exception exception) when (
            exception is ArgumentException or JevConfigurationException or JevTransportException)
        {
            var message = StripParameter(exception.Message);
            scope.Complete(message, isError: true);
            var status = exception switch
            {
                ArgumentException => StatusCodes.Status400BadRequest,
                JevTransportException => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status503ServiceUnavailable,
            };
            return Results.Problem(detail: message, statusCode: status);
        }
    }

    public static JsonElement Required(JsonElement element, string name)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return element;
    }

    public static string StripParameter(string message)
    {
        var marker = " (Parameter '";
        var index = message.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? message : message[..index];
    }
}
