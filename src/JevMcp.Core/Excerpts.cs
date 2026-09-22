namespace JevMcp.Core;

/// <summary>Text excerpts sent to the model.</summary>
public static class Excerpts
{
    /// <summary>Marker appended to cut text so the model knows it received a partial excerpt.</summary>
    public const string TruncationMarker = " [\u2026truncated]";

    /// <summary>Cuts long text and appends the explicit truncation marker.</summary>
    public static string Truncate(string text, int maxChars)
    {
        return text.Length <= maxChars ? text : text[..maxChars] + TruncationMarker;
    }
}
