namespace JevMcp.App;

/// <summary>Parses localized playground help lines: <c>field_name|description</c>.</summary>
internal static class PlaygroundExampleFieldParser
{
    public static IReadOnlyList<(string Name, string Description)> Parse(string? fields)
    {
        if (string.IsNullOrWhiteSpace(fields))
        {
            return [];
        }

        var lines = new List<(string, string)>();
        foreach (var raw in fields.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = raw.IndexOf('|', StringComparison.Ordinal);
            if (separator <= 0 || separator >= raw.Length - 1)
            {
                continue;
            }

            lines.Add((raw[..separator].Trim(), raw[(separator + 1)..].Trim()));
        }

        return lines;
    }
}
