using System.Globalization;
using JevMcp.App;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace JevMcp.Tests.App;

public sealed class ProjectReadmeSourceTests
{
    [Fact]
    public async Task LoadsEnglishReadmeFromEmbeddedResource()
    {
        var source = new ProjectReadmeSource(new FakeWebHostEnvironment(Path.GetTempPath()));

        var markdown = await source.GetMarkdownAsync(CultureInfo.GetCultureInfo("en"));

        Assert.NotNull(markdown);
        Assert.True(markdown.Length > 1000);
        Assert.Contains("JEV-MCP", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Comparison with the original", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadsPortugueseReadmeForPtBrCulture()
    {
        var source = new ProjectReadmeSource(new FakeWebHostEnvironment(Path.GetTempPath()));

        var markdown = await source.GetMarkdownAsync(CultureInfo.GetCultureInfo("pt-BR"));

        Assert.NotNull(markdown);
        Assert.Contains("Comparação com o original", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Comparison with the original", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadsSpanishReadmeForEsCulture()
    {
        var source = new ProjectReadmeSource(new FakeWebHostEnvironment(Path.GetTempPath()));

        var markdown = await source.GetMarkdownAsync(CultureInfo.GetCultureInfo("es"));

        Assert.NotNull(markdown);
        Assert.Contains("Comparación con el original", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pt-BR", "JevMcp.App.Readme.PtBr.md")]
    [InlineData("en", "JevMcp.App.Readme.en.md")]
    [InlineData("es", "JevMcp.App.Readme.Es.md")]
    public void EmbeddedResourceNameMatchesCulture(string culture, string expected)
    {
        Assert.Equal(expected, ProjectReadmeSource.ResourceName(culture));
    }

    private sealed class FakeWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "JevMcp.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = contentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
