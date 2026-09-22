using System.Text.Json;

namespace JevMcp.Data;

/// <summary>
/// The action lives in the tool JSON, with different names per contract. The writer
/// does not know each result type: it reads whatever is an action and, if several, keeps the worst.
/// </summary>
internal static class ResultingActionParser
{
    private static readonly Dictionary<string, int> Rank = new(StringComparer.Ordinal)
    {
        ["block"] = 40,
        ["escalate"] = 30,
        ["review"] = 20,
        ["skip"] = 10,
        ["auto"] = 0,
        ["pass"] = 0,
    };

    public static string? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return FromElement(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FromElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryString(root, "action", out var action))
        {
            return action;
        }

        if (root.TryGetProperty("recommendation", out var recommendation) &&
            TryString(recommendation, "action", out action))
        {
            return action;
        }

        if (root.TryGetProperty("overall", out var overall) &&
            TryString(overall, "decision", out action))
        {
            return action;
        }

        string? worst = null;
        if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in results.EnumerateArray())
            {
                if (TryString(item, "action", out var itemAction) ||
                    TryString(item, "decision", out itemAction) ||
                    TryRanked(item, "status", out itemAction))
                {
                    worst = Worse(worst, itemAction);
                }
            }
        }

        if (worst is not null)
        {
            return worst;
        }

        if (TryString(root, "exists_verdict", out var verdict))
        {
            return verdict;
        }

        return InferFromToolShape(root);
    }

    /// <summary>Legacy payloads that predate an explicit <c>action</c> field.</summary>
    private static string? InferFromToolShape(JsonElement root)
    {
        if (!TryString(root, "tool", out var tool))
        {
            return null;
        }

        if (string.Equals(tool, "jev_rerank", StringComparison.Ordinal))
        {
            if (TryString(root, "status", out var status) &&
                string.Equals(status, "invalid_response", StringComparison.Ordinal))
            {
                return "escalate";
            }

            if (root.TryGetProperty("ranked", out var ranked) && ranked.ValueKind == JsonValueKind.Array)
            {
                return "auto";
            }
        }

        return null;
    }

    private static bool TryRanked(JsonElement element, string name, out string value)
    {
        if (TryString(element, name, out value) && Rank.ContainsKey(value))
        {
            return true;
        }

        value = "";
        return false;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { Length: > 0 } text)
        {
            value = text;
            return true;
        }

        value = "";
        return false;
    }

    private static string? Worse(string? current, string candidate)
    {
        if (current is null)
        {
            return candidate;
        }

        var currentRank = Rank.GetValueOrDefault(current);
        var candidateRank = Rank.GetValueOrDefault(candidate);
        return candidateRank >= currentRank ? candidate : current;
    }
}
