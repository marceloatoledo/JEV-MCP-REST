using System.Text.Json;
using JevMcp.Core;

namespace JevMcp.Tools.Mcp;

/// <summary>
/// Reading of the tools' structured inputs. The SDK delivers these fields as raw JSON;
/// validating here is what turns a crooked payload into a useful error message instead of
/// a deserialization exception.
/// </summary>
internal static class JsonInput
{
    /// <summary>List of `{id?, text}`.</summary>
    public static IReadOnlyList<TextItem> TextItems(JsonElement element, string field)
    {
        return [.. Array(element, field).Select(item => new TextItem(
            OptionalString(item, "id", field),
            RequiredString(item, "text", field)))];
    }

    /// <summary>List of `{id?, description}` from the classification catalog.</summary>
    public static IReadOnlyList<ClassDefinition> Classes(JsonElement element)
    {
        return [.. Array(element, "classes").Select(item => new ClassDefinition(
            OptionalString(item, "id", "classes"),
            RequiredString(item, "description", "classes")))];
    }

    /// <summary>List of `{id, description}` of the alternatives in a decision.</summary>
    public static IReadOnlyList<DecideCandidate> DecideCandidates(JsonElement element)
    {
        return [.. Array(element, "candidates").Select(item => new DecideCandidate(
            RequiredString(item, "id", "candidates"),
            RequiredString(item, "description", "candidates")))];
    }

    /// <summary>List of `{id, pattern, description, flags?}` of the fields to extract.</summary>
    public static IReadOnlyList<ExtractField> ExtractFields(JsonElement element)
    {
        return [.. Array(element, "fields").Select(item => new ExtractField(
            RequiredString(item, "id", "fields"),
            RequiredString(item, "pattern", "fields"),
            RequiredString(item, "description", "fields"),
            OptionalString(item, "flags", "fields")))];
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"{field} must be an array of objects.", field);
        }

        return element.EnumerateArray();
    }

    private static string RequiredString(JsonElement item, string property, string field)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"each entry of {field} must have a {property} string.", field);
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement item, string property, string field)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"each entry of {field} must be an object.", field);
        }

        return item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
