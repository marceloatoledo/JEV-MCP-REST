using System.Xml.Linq;

namespace JevMcp.Tests;

/// <summary>
/// Project dependencies flow in one direction only:
/// App -> Tools and App -> Data; Tools and Data -> Providers -> Core.
/// Requirement "Separacao de camadas" in openspec/specs/deployment.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["JevMcp.Core"] = [],
        ["JevMcp.Providers"] = ["JevMcp.Core"],
        ["JevMcp.Tools"] = ["JevMcp.Providers"],
        ["JevMcp.Data"] = ["JevMcp.Providers"],
        ["JevMcp.App"] = ["JevMcp.Tools", "JevMcp.Data"],
    };

    [Theory]
    [InlineData("JevMcp.Core")]
    [InlineData("JevMcp.Providers")]
    [InlineData("JevMcp.Tools")]
    [InlineData("JevMcp.Data")]
    [InlineData("JevMcp.App")]
    public void ProjectReferencesOnlyTheLayerImmediatelyBelow(string project)
    {
        var actual = ProjectReferencesOf(ProjectCsprojPath(project));

        Assert.Equal(Allowed[project].Order(), actual.Order());
    }

    [Fact]
    public void NoCsprojDeclaresTheTargetFramework()
    {
        var offenders = Directory
            .EnumerateFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => XDocument.Load(path).Descendants("TargetFramework").Any())
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string ProjectCsprojPath(string project) =>
        Path.Combine(RepositoryRoot(), "src", project, $"{project}.csproj");

    private static string[] ProjectReferencesOf(string csprojPath) =>
        [.. XDocument.Load(csprojPath)
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value))];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JevMcp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("JevMcp.slnx not found above the test output directory.");
    }
}
