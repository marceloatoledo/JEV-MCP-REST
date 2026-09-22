using System.Text.Json;
using JevMcp.Core;

namespace JevMcp.Tools;

/// <summary>
/// Evidence accepts three forms at the tool input, as in the original: a single
/// document as text, an `{id?, text}` item, or a list of items.
/// </summary>
public static class EvidenceInput
{
    public static IReadOnlyList<TextItem> Parse(JsonElement evidence)
    {
        return evidence.ValueKind switch
        {
            // A single document gets a fixed id; an item without an id gets an indexed fallback.
            JsonValueKind.String => [new TextItem(Evidence.FallbackPrefix, evidence.GetString()!)],
            JsonValueKind.Object => [ItemOf(evidence)],
            JsonValueKind.Array => [.. evidence.EnumerateArray().Select(ItemOf)],
            _ => throw new ArgumentException(
                "evidence must be a string, an object with a text field, or an array of such objects.",
                nameof(evidence)),
        };
    }

    private static TextItem ItemOf(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("text", out var text) ||
            text.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("each evidence item must be an object with a text field.", nameof(element));
        }

        var id = element.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : null;

        return new TextItem(id, text.GetString()!);
    }
}
