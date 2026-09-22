using System.Xml.Linq;

namespace Scarlet.Sass.MSBuild.Tests;

/// <summary>
/// Checks that every runtime package's props file declares the pack the resolver expects.
/// </summary>
/// <remarks>
/// The props files are near-identical copies, and most of them describe a platform this test run can never
/// execute on. A swapped RID would otherwise only surface on somebody else's CI leg.
/// </remarks>
public class RuntimePackagePropsTests
{
    [Theory]
    [InlineData(Platform.WindowsX64)]
    [InlineData(Platform.WindowsArm64)]
    [InlineData(Platform.LinuxX64)]
    [InlineData(Platform.LinuxArm64)]
    [InlineData(Platform.LinuxMuslX64)]
    [InlineData(Platform.LinuxMuslArm64)]
    [InlineData(Platform.MacOsX64)]
    [InlineData(Platform.MacOsArm64)]
    public void RuntimePackageProps_ShouldDeclareThePackTheResolverLooksFor(Platform platform)
    {
        // Arrange
        var packageId = SassRuntimeResolver.GetRuntimePackageName(platform);
        var expectedRid = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var propsPath = Path.Combine(RepositoryRoot.Path, "src", packageId, "build", $"{packageId}.props");

        Assert.True(File.Exists(propsPath), $"Props file not found: {propsPath}");

        // Act
        var project = XDocument.Load(propsPath).Root;
        Assert.NotNull(project);

        var pack = Assert.Single(project.Descendants("SassRuntimePack"));

        // Assert
        Assert.Equal(packageId, pack.Attribute("Include")?.Value);
        Assert.Equal(expectedRid, pack.Element(SassRuntimePack.RidMetadataName)?.Value);

        var runtimesPath = pack.Element(SassRuntimePack.RuntimesPathMetadataName)?.Value;
        Assert.NotNull(runtimesPath);
        Assert.Contains("runtimes", runtimesPath);

        Assert.False(string.IsNullOrWhiteSpace(pack.Element(SassRuntimePack.VariantMetadataName)?.Value));
        Assert.True(int.TryParse(pack.Element(SassRuntimePack.PriorityMetadataName)?.Value, out _));
    }

    [Fact]
    public void RuntimePackageProps_ShouldHaveNoStragglersInSrc()
    {
        // Arrange - an extra runtime package the resolver knows nothing about would go unnoticed otherwise
        var expected = new[]
        {
            Platform.WindowsX64, Platform.WindowsArm64, Platform.LinuxX64,
            Platform.LinuxArm64, Platform.LinuxMuslX64, Platform.LinuxMuslArm64,
            Platform.MacOsX64, Platform.MacOsArm64
        }.Select(SassRuntimeResolver.GetRuntimePackageName).OrderBy(name => name, StringComparer.Ordinal);

        // Act
        var onDisk = Directory
            .EnumerateDirectories(Path.Combine(RepositoryRoot.Path, "src"), "Scarlet.Sass.Runtime.*")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal);

        // Assert
        Assert.Equal(expected, onDisk);
    }
}
