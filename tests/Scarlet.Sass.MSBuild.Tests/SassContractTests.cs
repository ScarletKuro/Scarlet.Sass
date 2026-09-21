using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Scarlet.Sass.MSBuild;

namespace Scarlet.Sass.MSBuild.Tests;

public class SassContractTests
{
    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64", "windows-x64", "sass.bat")]
    [InlineData(Platform.WindowsArm64, "win-arm64", "windows-arm64", "sass.bat")]
    [InlineData(Platform.LinuxX64, "linux-x64", "linux-x64", "sass")]
    [InlineData(Platform.LinuxArm64, "linux-arm64", "linux-arm64", "sass")]
    [InlineData(Platform.LinuxMuslX64, "linux-musl-x64", "linux-x64-musl", "sass")]
    [InlineData(Platform.LinuxMuslArm64, "linux-musl-arm64", "linux-arm64-musl", "sass")]
    [InlineData(Platform.MacOsX64, "osx-x64", "macos-x64", "sass")]
    [InlineData(Platform.MacOsArm64, "osx-arm64", "macos-arm64", "sass")]
    public void PlatformMap_UsesDartSassArchiveAndLayout(Platform platform, string rid, string downloadName, string executableName)
    {
        Assert.Equal(rid, SassRuntimeResolver.GetRuntimeIdentifier(platform));
        Assert.Equal("dart-sass", SassRuntimeResolver.GetRuntimeDirectoryName(platform));
        Assert.Equal(downloadName, SassRuntimeResolver.GetDownloadName(platform));
        Assert.Equal(executableName, SassRuntimeResolver.GetExecutableName(platform));

        var executablePath = SassRuntimeResolver.GetExecutablePath("runtimes", platform);

        Assert.EndsWith(Path.Combine(rid, "native", "dart-sass", executableName), executablePath);
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

    [Fact]
    public void Targets_PassEveryPublicSettingToCompileTask()
    {
        var targets = File.ReadAllText(Path.Combine(
            RepositoryRoot.Path,
            "src",
            "Scarlet.Sass.MSBuild",
            "build",
            "Scarlet.Sass.MSBuild.targets"));

        Assert.Contains("TaskName=\"Scarlet.Sass.MSBuild.SassCompileTask\"", targets);
        Assert.Contains("BeforeTargets=\"DispatchToInnerBuilds;ResolveProjectStaticWebAssets;PreBuildEvent\"", targets);
        Assert.Contains("Compilations=\"@(SassBeforeStaticWebAssets)\"", targets);
        Assert.Contains("OutputStyle=\"$(SassOutputStyle)\"", targets);
        Assert.Contains("SourceMap=\"$(SassSourceMap)\"", targets);
        Assert.Contains("EmbedSources=\"$(SassEmbedSources)\"", targets);
        Assert.Contains("QuietDeps=\"$(SassQuietDeps)\"", targets);
        Assert.Contains("LoadPaths=\"$(SassLoadPaths)\"", targets);
        Assert.Contains("PkgImporter=\"$(SassPkgImporter)\"", targets);
        Assert.Contains("SilenceDeprecations=\"$(SassSilenceDeprecations)\"", targets);
        Assert.Contains("FatalDeprecations=\"$(SassFatalDeprecations)\"", targets);
        Assert.Contains("AdditionalArguments=\"$(SassAdditionalArguments)\"", targets);
        Assert.Contains("StampDirectory=\"$(_SassStampDirectory)\"", targets);
        Assert.Contains("RuntimePacks=\"@(SassRuntimePack)\"", targets);
        Assert.Contains("ItemName=\"_SassGeneratedFiles\"", targets);
        Assert.Contains("<FileWrites Include=\"@(_SassGeneratedFiles)\"", targets);
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
}
