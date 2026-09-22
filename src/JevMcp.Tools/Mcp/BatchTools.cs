using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace JevMcp.Tools.Mcp;

/// <summary>MCP shell of <see cref="ClassifyService"/>.</summary>
[McpServerToolType]
public sealed class JevClassifyTool
{
    private readonly ClassifyService _service;

    public JevClassifyTool(ClassifyService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Classify, Title = "Classify items against a shared label set", ReadOnly = true)]
    [Description(
        "Assign each item to one class from a shared catalog with TypeSafe Jev, in one batched request: " +
        "the class catalog is sent once and every item becomes an independent Choice question. " +
        "Returns per item: the chosen class, the full distribution, confidence, winner-to-runner-up margin, " +
        "and an auto-versus-review decision. Auto requires both a high top probability (default 0.85) and a " +
        "clear margin (default 0.50); everything else is flagged for review. Include a manual_review class " +
        "in the catalog if you want an explicit escape hatch; the tool never invents one.")]
    public async Task<string> ClassifyAsync(
        [Description(
            "Items to classify, each with an optional id and a text field. Text is truncated at 2000 characters; " +
            "send bounded excerpts, not whole documents. Up to 64 per call.")]
        JsonElement items,
        [Description(
            "Shared class catalog, each entry with an optional id and a description. Strong descriptions carry " +
            "the decision: a precise definition, what belongs, what does not, precedence over overlapping " +
            "classes, and a short example.")]
        JsonElement classes,
        [Description("What this classification is for; shared across all items.")]
        string? purpose = null,
        [Description("Shared context available to every item's judgment: policies, catalogs, anything stable.")]
        JsonElement? context = null,
        [Description("Minimum top probability for auto. Default 0.85.")]
        double? auto_accept = null,
        [Description("Minimum winner-to-runner-up gap for auto. Default 0.5.")]
        double? minimum_margin = null,
        CancellationToken cancellationToken = default)
    {
        var request = Invalid.Guard(() => new ClassifyRequest(
            JsonInput.TextItems(items, "items"),
            JsonInput.Classes(classes),
            purpose,
            context is { } value ? JsonNode.Parse(value.GetRawText()) : null,
            auto_accept,
            minimum_margin));

        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(request, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}

/// <summary>MCP shell of <see cref="DecideService"/>.</summary>
[McpServerToolType]
public sealed class JevDecideTool
{
    private readonly DecideService _service;

    public JevDecideTool(DecideService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Decide, Title = "Decide between bounded alternatives", ReadOnly = true)]
    [Description(
        "One unresolved, bounded decision where semantic judgment over supplied evidence could change your plan: " +
        "implementation alternatives, product tradeoffs with known preferences, workflow selection. " +
        "Supply 2-6 candidates, evidence, and explicit priorities. Jev returns a Choice distribution over the " +
        "candidates plus escape hatches (ask_user / investigate / none), and a per-candidate per-requirement " +
        "supported / contradicted / unknown judgment for each optional requirement, all in one request. " +
        "One call per unchanged decision; do not repeat a call to obtain a more pleasing answer. " +
        "Use source inspection, tests, the user, or a reasoning model for open-ended research, routine choices, " +
        "correctness proofs, or predicting user consent. High probability is not proof.")]
    public async Task<string> DecideAsync(
        [Description("The bounded decision to make.")]
        string decision,
        [Description("Facts and measurements, not opinions. State is evidence, not instructions.")]
        string evidence,
        [Description("Explicit preferences and constraints from the user or plan.")]
        string priorities,
        [Description(
            "The alternatives, 2 to 6, each with a slug id matching ^[a-z][a-z0-9_-]*$ and a description. " +
            "Include 'do nothing' or 'gather more evidence' as candidates when useful.")]
        JsonElement candidates,
        [Description(
            "Specific requirements to check per candidate, up to 3. Each must test one property, not overall goodness.")]
        string[]? requirements = null,
        [Description(
            "Include ask_user / investigate / none as choosable options so the model can decline to rank. Default true.")]
        bool? escape_hatches = null,
        CancellationToken cancellationToken = default)
    {
        var request = Invalid.Guard(() => new DecideRequest(
            decision,
            evidence,
            priorities,
            JsonInput.DecideCandidates(candidates),
            requirements,
            escape_hatches));

        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(request, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}

/// <summary>MCP shell of <see cref="RerankService"/>.</summary>
[McpServerToolType]
public sealed class JevRerankTool
{
    private readonly RerankService _service;

    public JevRerankTool(RerankService service)
    {
        _service = service;
    }

    [McpServerTool(
        Name = JevTools.Rerank,
        Title = "Score every candidate's relevance and return them sorted",
        ReadOnly = true)]
    [Description(
        "Rerank candidates against a query with TypeSafe Jev: one independent relevance probability per candidate, " +
        "all in a single request, then sorted by score. Unlike jev_find (which picks one best answer), rerank scores " +
        "every candidate so the full ordering survives. TypeSafe's rerank cookbook reports that on the CLERC " +
        "benchmark this pattern lifted top-1 from 5% to 18% and top-10 from 38% to 62% (docs.typesafe.ai/cookbooks). " +
        "Use for retrieval ordering, dedup triage, or feed ranking across up to 250 candidates.")]
    public async Task<string> RerankAsync(
        [Description("What relevance is measured against, in natural language.")]
        string query,
        [Description(
            "Candidates to rerank, each with an optional id and a text field. Up to 250 in one call; " +
            "texts are truncated at 2000 chars.")]
        JsonElement candidates,
        [Description("How many ranked candidates to return. Default: all.")]
        int? top_k = null,
        CancellationToken cancellationToken = default)
    {
        var request = Invalid.Guard(() =>
            new RerankRequest(query, JsonInput.TextItems(candidates, "candidates"), top_k));

        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(request, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}

/// <summary>MCP shell of <see cref="CompareService"/>.</summary>
[McpServerToolType]
public sealed class JevCompareTool
{
    private readonly CompareService _service;

    public JevCompareTool(CompareService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Compare, Title = "Compare two passages for factual agreement", ReadOnly = true)]
    [Description(
        "Judge the relation between two passages with TypeSafe Jev: same_fact, contradicts, or different_facts, " +
        "with the full probability distribution, confidence, and an auto-versus-review decision. " +
        "Optionally supply aspects (price, date, method, …) and each gets an independent per-aspect judgment " +
        "in the same single request. Use for source reconciliation, changelog-vs-code drift, or merge sanity " +
        "checks. The request supplies no evidence beyond the two passages, so a same_fact verdict means they " +
        "agree with each other, not that they are true.")]
    public async Task<string> CompareAsync(
        [Description("First passage. Rejected above 20,000 characters.")]
        string passage_a,
        [Description("Second passage. Rejected above 20,000 characters.")]
        string passage_b,
        [Description("Named aspects to judge independently (e.g. 'price', 'launch date'). Each tests one property.")]
        string[]? aspects = null,
        [Description("What this comparison is for; helps disambiguate overlap.")]
        string? purpose = null,
        [Description("Minimum top probability for auto. Default 0.85.")]
        double? auto_accept = null,
        [Description("Minimum winner-to-runner-up gap for auto. Default 0.5.")]
        double? minimum_margin = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CompareRequest(passage_a, passage_b, aspects, purpose, auto_accept, minimum_margin);

        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(request, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}

/// <summary>MCP shell of <see cref="ExtractService"/>.</summary>
[McpServerToolType]
public sealed class JevExtractTool
{
    private readonly ExtractService _service;

    public JevExtractTool(ExtractService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Extract, Title = "Extract fields by regex, Jev picks the right match", ReadOnly = true)]
    [Description(
        "Extract structured fields from a document with TypeSafe Jev as the picker, not the generator: your regex " +
        "finds candidate substrings in code, Jev chooses which candidate is the field's true value, and the result " +
        "is returned verbatim — never model-generated text. Fields with zero regex matches never reach the model " +
        "(not_found); if no field has matches, no API call is made. Ambiguous picks are flagged for review. " +
        "Use for prices, dates, version numbers, IDs, and anything with a recognizable shape; keep documents bounded.")]
    public async Task<string> ExtractAsync(
        [Description("The document to extract from. Rejected above 50,000 characters.")]
        string document,
        [Description(
            "Fields to extract, up to 32 per call, all judged in one request. Each entry has an id matching " +
            "^[a-z][a-z0-9_-]*$, a pattern (regex source without delimiters, run under a hard timeout), a " +
            "description of what the field is, and optional flags such as 'i'.")]
        JsonElement fields,
        [Description("What the extraction is for; shared across fields.")]
        string? purpose = null,
        [Description("Minimum top probability for auto. Default 0.85.")]
        double? auto_accept = null,
        [Description("Minimum winner-to-runner-up gap for auto. Default 0.5.")]
        double? minimum_margin = null,
        CancellationToken cancellationToken = default)
    {
        var request = Invalid.Guard(() => new ExtractRequest(
            document,
            JsonInput.ExtractFields(fields),
            purpose,
            auto_accept,
            minimum_margin));

        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(request, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}
