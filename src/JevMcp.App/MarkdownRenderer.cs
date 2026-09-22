using System.Text.RegularExpressions;
using Markdig;

namespace JevMcp.App;

public interface IMarkdownRenderer
{
    string ToHtml(string markdown);
}

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly Regex ExternalLink = new(
        "<a href=\"(?<url>https?://[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string ToHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var html = Markdown.ToHtml(markdown, Pipeline);
        return ExternalLink.Replace(
            html,
            "<a href=\"${url}\" target=\"_blank\" rel=\"noopener noreferrer\"");
    }
}
