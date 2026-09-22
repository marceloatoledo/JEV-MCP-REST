namespace JevMcp.Tools;

/// <summary>
/// Item with the caller's id preserved and an opaque key for the model.
/// </summary>
/// <param name="External">Id returned to the caller, exactly as supplied.</param>
/// <param name="Key">Positional key used in the request.</param>
/// <param name="Text">Already truncated text.</param>
public sealed record KeyedEntry(string External, string Key, string Text);

/// <summary>
/// Positional keys for batches. The caller's id is never sanitized or renamed: it is
/// returned intact in the result, and the opaque key is what goes to the model. A
/// repeated id is an error, because a silent suffix would make the caller read the wrong result.
/// </summary>
public static class Keyed
{
    public static IReadOnlyList<KeyedEntry> Map<T>(
        IReadOnlyList<T> source,
        string name,
        string wirePrefix,
        Func<T, string?> id,
        Func<T, string> text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<KeyedEntry>(source.Count);

        for (var index = 0; index < source.Count; index++)
        {
            var item = source[index];
            var supplied = id(item);

            if (supplied is not null && !seen.Add(supplied))
            {
                throw new ArgumentException($"Duplicate {name} id: {supplied}", name);
            }

            entries.Add(new KeyedEntry(supplied ?? $"{name}{index}", $"{wirePrefix}{index}", text(item)));
        }

        return entries;
    }
}

/// <summary>Validation of the thresholds the tools receive from the caller.</summary>
public static class Thresholds
{
    /// <summary>Value in [0,1], using the tool default when the caller omits it.</summary>
    public static double Unit(double? value, double fallback, string name, string parameterName)
    {
        var resolved = value ?? fallback;

        if (!double.IsFinite(resolved) || resolved < 0 || resolved > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{name} must be between 0 and 1.");
        }

        return resolved;
    }
}
