namespace JevMcp.Core;

/// <summary>Normalization of evidence accepted by jev_verify, jev_review, and jev_gate.</summary>
public static class Evidence
{
    /// <summary>Fallback prefix for evidence ids.</summary>
    public const string FallbackPrefix = "evidence";

    /// <summary>A single text block becomes an item with a fixed id and no index.</summary>
    public static IReadOnlyList<IdentifiedItem> Normalize(string text)
    {
        return Identifiers.EnsureUniqueIds([new TextItem(FallbackPrefix, text)], FallbackPrefix).Items;
    }

    /// <summary>A list of items keeps supplied ids and gets an indexed fallback on the rest.</summary>
    public static IReadOnlyList<IdentifiedItem> Normalize(IEnumerable<TextItem> items)
    {
        return Identifiers.EnsureUniqueIds(items, FallbackPrefix).Items;
    }

    /// <summary>True when at least one item carries text that is not only whitespace.</summary>
    public static bool HasNonEmpty(IEnumerable<IdentifiedItem> items)
    {
        return items.Any(item => !string.IsNullOrWhiteSpace(item.Text));
    }
}
