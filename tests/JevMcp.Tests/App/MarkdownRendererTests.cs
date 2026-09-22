using JevMcp.App;

namespace JevMcp.Tests.App;

public sealed class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public void RendersHeadingsAndCodeWithoutRawHtml()
    {
        var html = _renderer.ToHtml("## Title\n\n`code` and <script>alert(1)</script>");

        Assert.Contains("<h2", html, StringComparison.Ordinal);
        Assert.Contains("<code>code</code>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExternalLinksOpenInNewTab()
    {
        var html = _renderer.ToHtml("[docs](https://example.com/path)");

        Assert.Contains("target=\"_blank\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
        Assert.Contains("https://example.com/path", html, StringComparison.Ordinal);
    }
}
