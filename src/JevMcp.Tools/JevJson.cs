using System.Text.Json;
using System.Text.Json.Nodes;
using JevMcp.Core;
using JevMcp.Providers;

namespace JevMcp.Tools;

/// <summary>
/// Serialization of tool results. The output format is contract: agents written for the
/// original jev-mcp need to work here without adaptation, so field names are snake_case
/// and the indentation is the same.
/// </summary>
public static class JevJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static string Serialize<T>(T payload) => JsonSerializer.Serialize(payload, Options);
}

/// <summary>Enum names in the payload. The value is contract, not an implementation detail.</summary>
internal static class JevWireNames
{
    public const string InvalidResponse = "invalid_response";

    public const string UnknownVerdict = "unknown";

    public static string Of(PolicyAction action)
    {
        return action switch
        {
            PolicyAction.Auto => "auto",
            PolicyAction.Review => "review",
            PolicyAction.Escalate => "escalate",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    public static string Of(ScreenAction action)
    {
        return action switch
        {
            ScreenAction.Pass => "pass",
            ScreenAction.Review => "review",
            ScreenAction.Block => "block",
            ScreenAction.Skip => "skip",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    public static string Of(ClaimVerdict verdict)
    {
        return verdict switch
        {
            ClaimVerdict.Verified => "verified",
            ClaimVerdict.Contradicted => "contradicted",
            ClaimVerdict.Unsupported => "unsupported",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict)),
        };
    }

    public static string Of(ExistsVerdict verdict)
    {
        return verdict switch
        {
            ExistsVerdict.Answered => "answered",
            ExistsVerdict.Partial => "partial",
            ExistsVerdict.Absent => "absent",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict)),
        };
    }

    public static string Of(JevProviderKind provider) => provider.ToString().ToLowerInvariant();
}

/// <summary>Assembly of the state sent to the model.</summary>
internal static class JevState
{
    /// <summary>Items `{id, text}` in the order the caller sent them.</summary>
    public static JsonArray ItemsOf(IEnumerable<IdentifiedItem> items)
    {
        var array = new JsonArray();

        foreach (var item in items)
        {
            array.Add(new JsonObject
            {
                ["id"] = item.Id,
                ["text"] = item.Text,
            });
        }

        return array;
    }
}
