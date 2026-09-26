using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Scarlet.Sass.MSBuild;

namespace Scarlet.Sass.MSBuild.Tests;

public class SassContractTests
{
    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64", "windows-x64", "sass.bat", "dart.exe", "zip")]
    [InlineData(Platform.WindowsArm64, "win-arm64", "windows-arm64", "sass.bat", "dart.exe", "zip")]
    [InlineData(Platform.LinuxX64, "linux-x64", "linux-x64", "sass", "dart", "tar.gz")]
    [InlineData(Platform.LinuxArm64, "linux-arm64", "linux-arm64", "sass", "dart", "tar.gz")]
    [InlineData(Platform.LinuxMuslX64, "linux-musl-x64", "linux-x64-musl", "sass", "dart", "tar.gz")]
    [InlineData(Platform.LinuxMuslArm64, "linux-musl-arm64", "linux-arm64-musl", "sass", "dart", "tar.gz")]
    [InlineData(Platform.MacOsX64, "osx-x64", "macos-x64", "sass", "dart", "tar.gz")]
    [InlineData(Platform.MacOsArm64, "osx-arm64", "macos-arm64", "sass", "dart", "tar.gz")]
    public void PlatformMap_UsesDartSassArchiveAndLayout(
        Platform platform,
        string rid,
        string downloadName,
        string launcherName,
        string dartExecutableName,
        string archiveExtension)
    {
        Assert.Equal(rid, SassRuntimeResolver.GetRuntimeIdentifier(platform));
        Assert.Equal("dart-sass", SassRuntimeResolver.GetRuntimeDirectoryName(platform));
        Assert.Equal(downloadName, SassRuntimeResolver.GetDownloadName(platform));
        Assert.Equal(archiveExtension, SassRuntimeResolver.GetArchiveExtension(platform));
        Assert.Equal(launcherName, SassRuntimeResolver.GetLauncherName(platform));

        var launcherPath = SassRuntimeResolver.GetLauncherPath("runtimes", platform);
        var permissionPaths = SassRuntimeResolver.GetExecutablePermissionPaths(launcherPath, platform);

        Assert.EndsWith(Path.Combine(rid, "native", "dart-sass", launcherName), launcherPath);
        Assert.EndsWith(Path.Combine(rid, "native", "dart-sass", "src", dartExecutableName), permissionPaths[1]);
    }

    [Fact]
    public void UnixDartSassBundle_RequiresLauncherAndInnerDartToBeExecutable()
    {
        var sassPath = Path.Combine("runtimes", "linux-x64", "native", "dart-sass", "sass");

        var paths = SassRuntimeResolver.GetExecutablePermissionPaths(sassPath, Platform.LinuxX64);

        Assert.Equal(
            [
                sassPath,
                Path.Combine("runtimes", "linux-x64", "native", "dart-sass", "src", "dart")
            ],
            paths);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64", "sass.bat", "dart.exe")]
    [InlineData(Platform.WindowsArm64, "win-arm64", "sass.bat", "dart.exe")]
    [InlineData(Platform.LinuxX64, "linux-x64", "sass", "dart")]
    [InlineData(Platform.LinuxArm64, "linux-arm64", "sass", "dart")]
    [InlineData(Platform.LinuxMuslX64, "linux-musl-x64", "sass", "dart")]
    [InlineData(Platform.LinuxMuslArm64, "linux-musl-arm64", "sass", "dart")]
    [InlineData(Platform.MacOsX64, "osx-x64", "sass", "dart")]
    [InlineData(Platform.MacOsArm64, "osx-arm64", "sass", "dart")]
    public void RuntimeProjects_DeclareTheOfficialDartSassLayout(
        Platform platform,
        string runtimeRid,
        string launcherName,
        string dartExecutableName)
    {
        var properties = LoadRuntimeProjectProperties(platform);

        Assert.Equal(runtimeRid, properties["RuntimeRid"]);
        Assert.Equal(launcherName, properties["SassLauncherName"]);
        Assert.Equal(dartExecutableName, properties["SassDartExecutableName"]);
    }

    [Fact]
    public void GetPlatform_DetectsMuslSeparatelyFromGlibc()
    {
        Assert.Equal(Platform.LinuxX64, SassRuntimeResolver.GetPlatform(OSPlatform.Linux, Architecture.X64, isMuslLibc: false, "Linux"));
        Assert.Equal(Platform.LinuxMuslX64, SassRuntimeResolver.GetPlatform(OSPlatform.Linux, Architecture.X64, isMuslLibc: true, "Linux"));
        Assert.Equal(Platform.LinuxArm64, SassRuntimeResolver.GetPlatform(OSPlatform.Linux, Architecture.Arm64, isMuslLibc: false, "Linux"));
        Assert.Equal(Platform.LinuxMuslArm64, SassRuntimeResolver.GetPlatform(OSPlatform.Linux, Architecture.Arm64, isMuslLibc: true, "Linux"));
    }

    [Fact]
    public void RuntimePackFactory_ParsesAndDeduplicatesItems()
    {
        var low = new TaskItem("Scarlet.Sass.Runtime.low");
        low.SetMetadata(SassRuntimePack.RidMetadataName, "win-x64");
        low.SetMetadata(SassRuntimePack.RuntimesPathMetadataName, "C:/low/runtimes");
        low.SetMetadata(SassRuntimePack.PriorityMetadataName, "0");

        var high = new TaskItem("Scarlet.Sass.Runtime.high");
        high.SetMetadata(SassRuntimePack.RidMetadataName, "win-x64");
        high.SetMetadata(SassRuntimePack.RuntimesPathMetadataName, "C:/high/runtimes");
        high.SetMetadata(SassRuntimePack.PriorityMetadataName, "10");

        var duplicateHigh = new TaskItem("Scarlet.Sass.Runtime.high");
        duplicateHigh.SetMetadata(SassRuntimePack.RidMetadataName, "win-x64");
        duplicateHigh.SetMetadata(SassRuntimePack.RuntimesPathMetadataName, "C:/high/runtimes");
        duplicateHigh.SetMetadata(SassRuntimePack.PriorityMetadataName, "10");

        var packs = SassRuntimePackFactory.FromTaskItems(new[] { low, high, duplicateHigh });
        var selected = SassRuntimeResolver.SelectPacks(packs, Platform.WindowsX64);

        Assert.Equal(2, packs.Count);
        Assert.Equal("Scarlet.Sass.Runtime.high", selected[0].Id);
        Assert.Equal(10, selected[0].Priority);
    }

    [Fact]
    public void Props_DefinePublicDefaultsWithoutJsonConfiguration()
    {
        var props = XDocument.Load(Path.Combine(
            RepositoryRoot.Path,
            "src",
            "Scarlet.Sass.MSBuild",
            "build",
            "Scarlet.Sass.MSBuild.props"));

        var propertyNames = props.Descendants().Where(static element => element.Parent?.Name.LocalName == "PropertyGroup").Select(static element => element.Name.LocalName).ToHashSet();

        Assert.Contains("SassEnabled", propertyNames);
        Assert.Contains("SassOutputStyle", propertyNames);
        Assert.Contains("SassSourceMap", propertyNames);
        Assert.Contains("SassEmbedSources", propertyNames);
        Assert.Contains("SassQuietDeps", propertyNames);
        Assert.Contains("SassLoadPaths", propertyNames);
        Assert.Contains("SassPkgImporter", propertyNames);
        Assert.Contains("SassRuntimeDownload", propertyNames);
        Assert.Contains("SassVersionDownload", propertyNames);
        Assert.DoesNotContain(propertyNames, static name => name.Contains("Json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, static name => name.Contains("AppSettings", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("build/Scarlet.Sass.MSBuild.targets")]
    [InlineData("buildMultiTargeting/Scarlet.Sass.MSBuild.targets")]
    [InlineData("Scarlet.Sass.MSBuild.targets")]
    public void Targets_ShouldWireEveryCompileTaskInput(string targetsRelativePath)
    {
        // Derived from the task rather than listed here on purpose. The hand-written list this replaced had
        // drifted to cover 12 of 19 inputs, and the gaps were invisible: an unwired input silently falls back
        // to its C# default, and every default agrees with what the tests already build. Configuration is the
        // worst of them - unwired, it stays "Debug", so a Release build would quietly emit expanded CSS with
        // source maps and embedded sources, and the settings stamp would stop changing between the two
        // configurations. Deleting an attribute has to fail here, not ship.
        var expected = typeof(SassCompileTask)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            // Outputs are carried by <Output> elements, not attributes, and have non-public setters.
            .Where(property => property.GetSetMethod() is not null)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var task = Assert.Single(
            LoadProject(targetsRelativePath).Descendants("SassCompileTask"));

        var actual = task.Attributes()
            .Select(attribute => attribute.Name.LocalName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, actual);

        // A wired-but-empty attribute passes the set check while behaving exactly like an unwired one.
        Assert.All(task.Attributes(), attribute => Assert.False(
            string.IsNullOrWhiteSpace(attribute.Value),
            $"{attribute.Name.LocalName} is wired to an empty value in {targetsRelativePath}."));
    }

    [Fact]
    public void Targets_ShouldPlumbGeneratedFilesIntoContentAndClean()
    {
        // The task's inputs moved to Targets_ShouldWireEveryCompileTaskInput, which derives them from the
        // task instead of listing them. What is left here is the plumbing on the other side of the call:
        // where generated files go and how clean finds them again.
        var targets = File.ReadAllText(Path.Combine(
            RepositoryRoot.Path,
            "src",
            "Scarlet.Sass.MSBuild",
            "build",
            "Scarlet.Sass.MSBuild.targets"));

        Assert.Contains("TaskName=\"Scarlet.Sass.MSBuild.SassCompileTask\"", targets);
        Assert.Contains("BeforeTargets=\"DispatchToInnerBuilds;ResolveProjectStaticWebAssets;PreBuildEvent\"", targets);
        Assert.DoesNotContain("GetRelativePath", targets);
        Assert.Contains("<_SassGeneratedContent Include=\"%(_SassGeneratedFiles.RelativePath)\"", targets);
        Assert.Contains("ItemName=\"_SassGeneratedFiles\"", targets);
        Assert.Contains("<FileWrites Include=\"@(_SassGeneratedFiles)\"", targets);
        Assert.Contains("Exists('$(_SassStampDirectory)/Sass.generated.txt')", targets);
        Assert.Contains("File=\"$(_SassStampDirectory)/Sass.generated.txt\"", targets);
        Assert.Contains("Files=\"$(_SassStampDirectory)/Sass.generated.txt;$(_SassStampDirectory)/Sass.settings.stamp\"", targets);
        Assert.Contains("Condition=\"'@(_SassFilesToClean)' != ''\"", targets);
        Assert.DoesNotContain("$(_SassStampDirectory)\\", targets);
    }

    [Theory]
    [InlineData("build/Scarlet.Sass.MSBuild.targets")]
    [InlineData("buildMultiTargeting/Scarlet.Sass.MSBuild.targets")]
    [InlineData("Scarlet.Sass.MSBuild.targets")]
    public void Targets_ShouldCleanGeneratedFilesInSingleAndMultiTargetedProjects(string targetsRelativePath)
    {
        var cleanTarget = Assert.Single(
            LoadProject(targetsRelativePath).Descendants("Target"),
            static target => string.Equals(target.Attribute("Name")?.Value, "SassClean", StringComparison.Ordinal));

        Assert.Equal("CoreClean;Clean", cleanTarget.Attribute("BeforeTargets")?.Value);
    }

    [Fact]
    public void RuntimeTargets_PackDartSassFolderWithoutDuplicatingRecursiveDirectory()
    {
        var targets = XDocument.Load(Path.Combine(RepositoryRoot.Path, "build", "SassRuntime.targets"));

        var runtimeItem = Assert.Single(
            targets.Descendants("None"),
            static element => (element.Attribute("Include")?.Value ?? string.Empty).Contains("dart-sass/**/*", StringComparison.Ordinal));

        Assert.Equal("true", runtimeItem.Attribute("Pack")?.Value);
        Assert.Equal("runtimes/$(RuntimeRid)/native/dart-sass/", runtimeItem.Attribute("PackagePath")?.Value);
    }

    [Fact]
    public void RuntimeTargets_ValidateTheFullOfficialDartSassLayout()
    {
        var targets = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "build", "SassRuntime.targets"));

        Assert.Contains("$(SassSourceDir)/$(SassLauncherName)", targets);
        Assert.Contains("$(SassSourceDir)/src/$(SassDartExecutableName)", targets);
        Assert.Contains("$(SassSourceDir)/src/sass.snapshot", targets);
        Assert.Contains("$(RidOutputDir)/$(SassLauncherName)", targets);
        Assert.Contains("$(RidOutputDir)/src/$(SassDartExecutableName)", targets);
    }

    private static XElement LoadProject(string targetsRelativePath)
    {
        var targetsPath = Path.Combine(
            RepositoryRoot.Path,
            "src",
            "Scarlet.Sass.MSBuild",
            targetsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(targetsPath), $"Targets file not found: {targetsPath}");

        var project = XDocument.Load(targetsPath).Root;
        Assert.NotNull(project);

        return project;
    }

    private static Dictionary<string, string> LoadRuntimeProjectProperties(Platform platform)
    {
        var projectName = SassRuntimeResolver.GetRuntimePackageName(platform);
        var projectPath = Path.Combine(RepositoryRoot.Path, "src", projectName, $"{projectName}.csproj");
        var project = XDocument.Load(projectPath);

        return project.Descendants()
            .Where(static element => element.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(static element => element.Name.LocalName)
            .ToDictionary(static group => group.Key, static group => group.Last().Value.Trim());
    }
}
