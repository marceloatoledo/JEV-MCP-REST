using System.ComponentModel;
using System.Text.Json;
using JevMcp.Core;
using JevMcp.Providers;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace JevMcp.Tools.Mcp;

// Parameter names are those of the original jev-mcp contract, including `auto_accept` and
// `top_k` in snake_case: they become the properties of the tool input schema, and an
// agent written for the original server has to work here without adapting the payload.

/// <summary>MCP shell of <see cref="VerifyService"/>.</summary>
[McpServerToolType]
public sealed class JevVerifyTool
{
    private readonly VerifyService _service;

    public JevVerifyTool(VerifyService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Verify, Title = "Verify claims against evidence", ReadOnly = true)]
    [Description(
        "Check each claim against provided evidence text with TypeSafe Jev. Returns per claim: " +
        "verdict (verified | contradicted | unsupported), full probability distribution, confidence, " +
        "and whether the verdict stands on its own (auto) or needs human review. " +
        "Pattern: docs.typesafe.ai/cookbooks/citation_check. Pass reports, PR descriptions, or agent briefs as claims " +
        "and their cited sources, diffs, or documents as evidence.")]
    public async Task<string> VerifyAsync(
        [Description("Claims to verify, e.g. individual factual statements from a report.")]
        string[] claims,
        [Description(
            "Evidence to check the claims against: a single document as text, one item with an optional id " +
            "and a text field, or an array of such items. With more than one item, each claim is also matched " +
            "to the item it rests on.")]
        JsonElement evidence,
        [Description(
            "Verdicts at or above this confidence stand automatically; below it they are flagged 'review'. Default 0.8.")]
        double? auto_accept = null,
        CancellationToken cancellationToken = default)
    {
        var request = Invalid.Guard(() => new VerifyRequest(claims, EvidenceInput.Parse(evidence), auto_accept));
        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(request, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}

/// <summary>MCP shell of <see cref="ScreenService"/>.</summary>
[McpServerToolType]
public sealed class JevScreenTool
{
    private readonly ScreenService _service;

    public JevScreenTool(ScreenService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Screen, Title = "Screen content before it enters agent context", ReadOnly = true)]
    [Description(
        "Judge fetched or external text with TypeSafe Jev before an agent reads it: probability it contains " +
        "instructions aimed at an AI agent (prompt injection), whether it has substantive content, and (when a purpose " +
        "is given) whether it is relevant to the task. Returns a recommendation: pass | review | block | skip. " +
        "Pattern: docs.typesafe.ai/cookbooks/llm_guardrails.")]
    public async Task<string> ScreenAsync(
        [Description("The content to screen, e.g. a fetched web page or pasted document.")]
        string text,
        [Description("What the consuming agent is trying to do; enables a relevance judgment and the 'skip' action.")]
        string? purpose = null,
        [Description("Injection probability at or above which content is blocked. Default 0.75.")]
        double? block_at = null,
        [Description("Injection probability at or above which content is flagged for review. Default 0.25.")]
        double? review_at = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ScreenRequest(text, purpose, block_at, review_at);
        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(request, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}

/// <summary>MCP shell of <see cref="FindService"/>.</summary>
[McpServerToolType]
public sealed class JevFindTool
{
    private readonly FindService _service;

    public JevFindTool(FindService service)
    {
        _service = service;
    }

    [McpServerTool(Name = JevTools.Find, Title = "Semantic search over candidates", ReadOnly = true)]
    [Description(
        "Rank candidates against a plain-language query with TypeSafe Jev — no embeddings needed. " +
        "One Choice scores every candidate id by how well it answers the query, plus a Noul checks whether " +
        "any candidate addresses the query at all (so a confident 'top hit' cannot masquerade as an answer). " +
        "Pattern: docs.typesafe.ai/cookbooks/semantic_find. Use for 'which file/note/line covers X' across up to " +
        "250 candidates.")]
    public async Task<string> FindAsync(
        [Description("What you are looking for, in natural language.")]
        string query,
        [Description(
            "Candidates to search, each with an optional id and a text field. Up to 250 in one call; " +
            "texts are truncated at 2000 chars.")]
        JsonElement candidates,
        [Description("How many ranked candidates to return. Default 5.")]
        int? top_k = null,
        CancellationToken cancellationToken = default)
    {
        var request = Invalid.Guard(() =>
            new FindRequest(query, JsonInput.TextItems(candidates, "candidates"), top_k));
        var result = await Invalid
            .GuardAsync(() => _service.JudgeAsync(request, cancellationToken))
            .ConfigureAwait(false);

        return JevJson.Serialize(result);
    }
}

/// <summary>
/// Failures the caller needs to read become a protocol error with the original message.
/// The SDK hides the message of any other exception, and "an error occurred" does not
/// tell an operator that a credential is missing nor an agent which limit it exceeded.
/// </summary>
internal static class Invalid
{
    public static T Guard<T>(Func<T> build)
    {
        try
        {
            return build();
        }
        catch (ArgumentException error)
        {
            throw Protocol(error);
        }
    }

    public static async Task<T> GuardAsync<T>(Func<Task<T>> run)
    {
        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (ArgumentException error)
        {
            throw Protocol(error);
        }
        catch (JevConfigurationException error)
        {
            throw new McpException(error.Message);
        }
        catch (JevTransportException error)
        {
            // The transport message already leaves the provider layer redacted.
            throw new McpException(error.Message);
        }
    }

    private static McpException Protocol(ArgumentException error)
    {
        // The "(Parameter 'request')" suffix talks about the C# argument, which does not
        // exist for whoever called the tool: the payload property is already in the message.
        var message = error.ParamName is { } name
            ? error.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal)
            : error.Message;

        return new McpException(message);
    }
}