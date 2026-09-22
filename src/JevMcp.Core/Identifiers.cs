using System.Text.RegularExpressions;

namespace JevMcp.Core;

/// <summary>Item supplied by the caller, with an optional id.</summary>
public sealed record TextItem(string? Id, string Text);

/// <summary>Item after normalization, with a safe id unique in the batch.</summary>
public sealed record IdentifiedItem(string Id, string Text);

/// <summary>Result of <see cref="Identifiers.EnsureUniqueIds"/>, with the renamed-id map.</summary>
public sealed record UniqueIdsResult(
    IReadOnlyList<IdentifiedItem> Items,
    IReadOnlyDictionary<string, string> Renamed);

/// <summary>
/// Normalization of caller ids into Choice option keys.
/// </summary>
public static partial class Identifiers
{
    /// <summary>
    /// Keeps alphanumerics, underscore, hyphen, and dot; collapses the rest into underscore,
    /// strips leading and trailing underscores, and cuts at 64 characters.
    /// </summary>
    public static string SanitizeId(string id)
    {
        var cleaned = TrimUnderscores().Replace(UnsafeCharacters().Replace(id, "_"), string.Empty);
        return cleaned.Length > 0 ? cleaned[..Math.Min(64, cleaned.Length)] : string.Empty;
    }

    /// <summary>
    /// Ensures every item has a safe unique id, using the fallback prefix plus the index
    /// when the caller did not supply a usable id.
    /// </summary>
    public static UniqueIdsResult EnsureUniqueIds(IEnumerable<TextItem> items, string fallbackPrefix)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<IdentifiedItem>();

        var index = 0;
        foreach (var item in items)
        {
            var raw = item.Id ?? string.Empty;
            var sanitized = SanitizeId(raw);
            var baseId = sanitized.Length > 0 ? sanitized : $"{fallbackPrefix}{index}";

            var id = baseId;
            var suffix = 1;
            while (!used.Add(id))
            {
                id = $"{baseId}_{suffix++}";
            }

            if (raw.Length > 0 && !string.Equals(raw, id, StringComparison.Ordinal))
            {
                renamed[raw] = id;
            }

            result.Add(new IdentifiedItem(id, item.Text));
            index++;
        }

        return new UniqueIdsResult(result, renamed);
    }

    [GeneratedRegex("[^A-Za-z0-9_.-]+")]
    private static partial Regex UnsafeCharacters();

    [GeneratedRegex("^_+|_+$")]
    private static partial Regex TrimUnderscores();
}
