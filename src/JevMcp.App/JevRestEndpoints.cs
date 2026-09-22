using System.Text.Json;
using System.Text.Json.Nodes;
using JevMcp.Data;
using JevMcp.Tools;
using JevMcp.Tools.Mcp;

namespace JevMcp.App;

internal static class JevRestEndpoints
{
    public static IEndpointRouteBuilder MapJevRest(this IEndpointRouteBuilder app)
    {
        var jev = app.MapGroup("/api/jev")
            .WithTags("Jev")
            .AllowAnonymous()
            .DisableAntiforgery();

        jev.MapGet("/tools", () => Results.Json(JevRestCatalog.Tools, JevJson.Options))
            .WithName("ListJevTools")
            .WithSummary("List the ten judgment tools")
            .Produces<JevRestToolInfo[]>();

        jev.MapPost("/verify", (Delegate)VerifyAsync)
            .WithName("JevVerify")
            .WithSummary("Verify claims against evidence")
            .WithDescription(JevRestCatalog.Of(JevTools.Verify).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/screen", (Delegate)ScreenAsync)
            .WithName("JevScreen")
            .WithSummary("Screen content before it enters agent context")
            .WithDescription(JevRestCatalog.Of(JevTools.Screen).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/find", (Delegate)FindAsync)
            .WithName("JevFind")
            .WithSummary("Semantic search over candidates")
            .WithDescription(JevRestCatalog.Of(JevTools.Find).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/classify", (Delegate)ClassifyAsync)
            .WithName("JevClassify")
            .WithSummary("Classify items against a shared label set")
            .WithDescription(JevRestCatalog.Of(JevTools.Classify).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/decide", (Delegate)DecideAsync)
            .WithName("JevDecide")
            .WithSummary("Decide between bounded alternatives")
            .WithDescription(JevRestCatalog.Of(JevTools.Decide).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/rerank", (Delegate)RerankAsync)
            .WithName("JevRerank")
            .WithSummary("Score every candidate's relevance and return them sorted")
            .WithDescription(JevRestCatalog.Of(JevTools.Rerank).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/compare", (Delegate)CompareAsync)
            .WithName("JevCompare")
            .WithSummary("Compare two passages for factual agreement")
            .WithDescription(JevRestCatalog.Of(JevTools.Compare).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/extract", (Delegate)ExtractAsync)
            .WithName("JevExtract")
            .WithSummary("Extract fields by regex, Jev picks the right match")
            .WithDescription(JevRestCatalog.Of(JevTools.Extract).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/review", (Delegate)ReviewAsync)
            .WithName("JevReview")
            .WithSummary("Review a proposed patch")
            .WithDescription(JevRestCatalog.Of(JevTools.Review).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        jev.MapPost("/gate", (Delegate)GateAsync)
            .WithName("JevGate")
            .WithSummary("Gate completion: review a patch and verify claims")
            .WithDescription(JevRestCatalog.Of(JevTools.Gate).Description)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static Task<IResult> VerifyAsync(
        HttpContext http,
        VerifyApiRequest body,
        VerifyService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Verify,
            body,
            () =>
            {
                var request = new VerifyRequest(
                    body.Claims,
                    EvidenceInput.Parse(JevRest.Required(body.Evidence, "evidence")),
                    body.AutoAccept);
                return service.JudgeAsync(request, cancellationToken);
            });
    }

    private static Task<IResult> ScreenAsync(
        HttpContext http,
        ScreenApiRequest body,
        ScreenService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Screen,
            body,
            () => service.JudgeAsync(
                new ScreenRequest(body.Text, body.Purpose, body.BlockAt, body.ReviewAt),
                cancellationToken));
    }

    private static Task<IResult> FindAsync(
        HttpContext http,
        FindApiRequest body,
        FindService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Find,
            body,
            () =>
            {
                var request = new FindRequest(
                    body.Query,
                    JsonInput.TextItems(JevRest.Required(body.Candidates, "candidates"), "candidates"),
                    body.TopK);
                return service.JudgeAsync(request, cancellationToken);
            });
    }

    private static Task<IResult> ClassifyAsync(
        HttpContext http,
        ClassifyApiRequest body,
        ClassifyService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Classify,
            body,
            () =>
            {
                var context = body.Context is { } value &&
                    value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null
                    ? JsonNode.Parse(value.GetRawText())
                    : null;
                var request = new ClassifyRequest(
                    JsonInput.TextItems(JevRest.Required(body.Items, "items"), "items"),
                    JsonInput.Classes(JevRest.Required(body.Classes, "classes")),
                    body.Purpose,
                    context,
                    body.AutoAccept,
                    body.MinimumMargin);
                return service.JudgeAsync(request, cancellationToken);
            });
    }

    private static Task<IResult> DecideAsync(
        HttpContext http,
        DecideApiRequest body,
        DecideService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Decide,
            body,
            () =>
            {
                var request = new DecideRequest(
                    body.Decision,
                    body.Evidence,
                    body.Priorities,
                    JsonInput.DecideCandidates(JevRest.Required(body.Candidates, "candidates")),
                    body.Requirements,
                    body.EscapeHatches);
                return service.JudgeAsync(request, cancellationToken);
            });
    }

    private static Task<IResult> RerankAsync(
        HttpContext http,
        RerankApiRequest body,
        RerankService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Rerank,
            body,
            () =>
            {
                var request = new RerankRequest(
                    body.Query,
                    JsonInput.TextItems(JevRest.Required(body.Candidates, "candidates"), "candidates"),
                    body.TopK);
                return service.JudgeAsync(request, cancellationToken);
            });
    }

    private static Task<IResult> CompareAsync(
        HttpContext http,
        CompareApiRequest body,
        CompareService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Compare,
            body,
            () => service.JudgeAsync(
                new CompareRequest(
                    body.PassageA,
                    body.PassageB,
                    body.Aspects,
                    body.Purpose,
                    body.AutoAccept,
                    body.MinimumMargin),
                cancellationToken));
    }

    private static Task<IResult> ExtractAsync(
        HttpContext http,
        ExtractApiRequest body,
        ExtractService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Extract,
            body,
            () =>
            {
                var request = new ExtractRequest(
                    body.Document,
                    JsonInput.ExtractFields(JevRest.Required(body.Fields, "fields")),
                    body.Purpose,
                    body.AutoAccept,
                    body.MinimumMargin);
                return service.JudgeAsync(request, cancellationToken);
            });
    }

    private static Task<IResult> ReviewAsync(
        HttpContext http,
        ReviewApiRequest body,
        ReviewService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Review,
            body,
            () => service.JudgeAsync(
                new ReviewRequest(body.Request, body.Diff, body.Tests, body.AutoAccept, body.ReviewAt, body.CompositeFloor),
                cancellationToken));
    }

    private static Task<IResult> GateAsync(
        HttpContext http,
        GateApiRequest body,
        GateService service,
        ICallAudit audit,
        CancellationToken cancellationToken)
    {
        return JevRest.RunAsync(
            http,
            audit,
            JevTools.Gate,
            body,
            () =>
            {
                var request = new GateRequest(
                    body.Request,
                    body.Diff,
                    body.Claims,
                    EvidenceInput.Parse(JevRest.Required(body.Evidence, "evidence")),
                    body.Tests,
                    body.AutoAccept,
                    body.ReviewAt,
                    body.CompositeFloor);
                return service.JudgeAsync(request, cancellationToken);
            });
    }
}
