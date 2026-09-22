using System.Globalization;
using System.Reflection;

namespace JevMcp.App;

public interface IProjectReadmeSource
{
    Task<string?> GetMarkdownAsync(CultureInfo? uiCulture = null, CancellationToken cancellationToken = default);
}

public sealed class ProjectReadmeSource(IWebHostEnvironment environment) : IProjectReadmeSource
{
    internal const string EnglishResourceName = "JevMcp.App.Readme.en.md";

    public async Task<string?> GetMarkdownAsync(
        CultureInfo? uiCulture = null,
        CancellationToken cancellationToken = default)
    {
        uiCulture ??= CultureInfo.CurrentUICulture;
        if (!UiCultures.TryNormalize(uiCulture.Name, out var culture))
            culture = UiCultures.Default;

        foreach (var (resourceName, resourceCulture) in EmbeddedResourceCandidates(culture))
        {
            var embedded = await ReadEmbeddedAsync(resourceName, resourceCulture, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(embedded))
                return embedded;
        }

        foreach (var path in CandidatePaths(environment, culture))
        {
            if (!File.Exists(path))
                continue;

            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    internal static IEnumerable<(string ResourceName, string Culture)> EmbeddedResourceCandidates(string culture)
    {
        yield return (ResourceName(culture), culture);
        if (!string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
            yield return (EnglishResourceName, "en");
    }

    internal static string ResourceName(string culture) => culture switch
    {
        "pt-BR" => "JevMcp.App.Readme.PtBr.md",
        "es" => "JevMcp.App.Readme.Es.md",
        "en" => EnglishResourceName,
        _ => EnglishResourceName,
    };

    private static async Task<string?> ReadEmbeddedAsync(
        string resourceName,
        string culture,
        CancellationToken cancellationToken)
    {
        var assembly = ResolveAssembly(culture);
        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Assembly ResolveAssembly(string culture)
    {
        var main = typeof(ProjectReadmeSource).Assembly;
        if (string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
            return main;

        return main.GetSatelliteAssembly(CultureInfo.GetCultureInfo(culture)) ?? main;
    }

    internal static IEnumerable<string> CandidatePaths(IWebHostEnvironment environment, string culture)
    {
        yield return Path.Combine(AppContext.BaseDirectory, $"readme.{culture}.md");
        yield return Path.Combine(environment.ContentRootPath, $"readme.{culture}.md");
        yield return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "docs", $"readme.{culture}.md"));

        if (string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(AppContext.BaseDirectory, "README.md");
            yield return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "README.md"));
        }
    }
}
