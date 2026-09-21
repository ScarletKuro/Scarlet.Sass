using System.Xml.Linq;

namespace Scarlet.Sass.MSBuild.Tests;

/// <summary>
/// The package ships <c>build/Scarlet.Sass.MSBuild.targets</c>, while the samples and integration tests
/// import <c>Scarlet.Sass.MSBuild.targets</c> from the source tree. The two are hand-maintained copies, so
/// anything proven against the development copy only holds for real consumers while they agree.
/// </summary>
public class SassTargetsTests
{
    private const string PackagedTargets = "build/Scarlet.Sass.MSBuild.targets";
    private const string DevelopmentTargets = "Scarlet.Sass.MSBuild.targets";

    [Fact]
    public void BothTargetsFiles_ShouldDefineTheSameTargets()
    {
        // Comparing the target bodies below only covers targets that exist in both files; without this, a
        // target added to one copy alone would go unnoticed.
        var packaged = LoadTargetNames(PackagedTargets);
        var development = LoadTargetNames(DevelopmentTargets);

        Assert.Equal(packaged, development);
    }

    [Theory]
    // The development copy has to build the task assembly before it can call into it, so it prefixes one
    // extra dependency; the packaged copy ships that assembly and must not carry it.
    [InlineData("_SassResolveStampDirectory", null)]
    [InlineData("Sass", null)]
    [InlineData("RunSassBeforeStaticWebAssets", "ResolveProjectReferences")]
    public void DevelopmentTargets_ShouldStayInSyncWithPackagedTargets(string targetName, string? developmentOnlyPrefix)
    {
        // Arrange
        var packagedTarget = LoadTarget(PackagedTargets, targetName);
        var developmentTarget = LoadTarget(DevelopmentTargets, targetName);

        var packagedDependsOn = packagedTarget.Attribute("DependsOnTargets")?.Value;
        var expectedDevelopmentDependsOn = developmentOnlyPrefix is null
            ? packagedDependsOn
            : $"{developmentOnlyPrefix};{packagedDependsOn}";

        Assert.Equal(expectedDevelopmentDependsOn, developmentTarget.Attribute("DependsOnTargets")?.Value);

        // Normalised away so the bodies can be compared as-is; every other difference is a failure.
        developmentTarget.SetAttributeValue("DependsOnTargets", packagedDependsOn);

        // Act & Assert
        Assert.True(
            XNode.DeepEquals(packagedTarget, developmentTarget),
            $"""
             The '{targetName}' target differs between the packaged and development targets files.

             {PackagedTargets}:
             {packagedTarget}

             {DevelopmentTargets}:
             {developmentTarget}
             """);
    }

    private static IReadOnlyList<string> LoadTargetNames(string targetsRelativePath) =>
        LoadProject(targetsRelativePath)
            .Elements("Target")
            .Select(target => target.Attribute("Name")?.Value ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static XElement LoadTarget(string targetsRelativePath, string targetName) =>
        // Detached from its document so the caller can strip the expected differences before comparing.
        new(Assert.Single(
            LoadProject(targetsRelativePath).Elements("Target"),
            target => string.Equals(target.Attribute("Name")?.Value, targetName, StringComparison.Ordinal)));

    private static XElement LoadProject(string targetsRelativePath)
    {
        var targetsPath = Path.Combine(
            RepositoryRoot.Path, "src", "Scarlet.Sass.MSBuild", targetsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(targetsPath), $"Targets file not found: {targetsPath}");

        var project = XDocument.Load(targetsPath).Root;
        Assert.NotNull(project);

        return project;
    }
}
