using System.IO.Abstractions.TestingHelpers;

namespace Scarlet.Sass.MSBuild.Tests;

public class SassRuntimeResolverTests
{
    private static MockFileSystem FileSystemWithSass(string runtimesPath, Platform platform)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(SassRuntimeResolver.GetLauncherPath(runtimesPath, platform), new MockFileData("fake executable"));

        return fileSystem;
    }

    [Fact]
    public void ResolveSassLauncher_WithNoRuntimeDirectory_ShouldThrowFileNotFoundException()
    {
        var platform = SassRuntimeResolver.GetCurrentPlatform();

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            SassRuntimeResolver.ResolveSassLauncher(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                platform,
                runtimeDirectory: null));
        Assert.Contains("Sass runtime package not found", exception.Message);
        Assert.Contains("Scarlet.Sass.Runtime", exception.Message);
    }

    [Fact]
    public void ResolveSassLauncher_WithMissingFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var runtimeDirectory = "/runtime";
        var platform = Platform.LinuxX64;
        var mockFileSystem = new MockFileSystem();

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            SassRuntimeResolver.ResolveSassLauncher(
                mockFileSystem,
                NoOpChmodProvider.Instance,
                platform,
                runtimeDirectory));

        Assert.Contains("Sass launcher not found at", exception.Message);
        Assert.Contains("Scarlet.Sass.Runtime.linux-x64", exception.Message);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64", "sass.bat")]
    [InlineData(Platform.LinuxX64, "linux-x64", "sass")]
    [InlineData(Platform.LinuxArm64, "linux-arm64", "sass")]
    [InlineData(Platform.MacOsX64, "osx-x64", "sass")]
    [InlineData(Platform.MacOsArm64, "osx-arm64", "sass")]
    public void ResolveSassLauncher_WithValidFile_ShouldReturnPath(
        Platform platform,
        string runtimeId,
        string launcherName)
    {
        // Arrange
        var runtimeDirectory = "/runtime";
        var expectedPath = Path.GetFullPath(Path.Combine(runtimeDirectory, runtimeId, "native", "dart-sass", launcherName));

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("fake executable"));

        // Act
        var result = SassRuntimeResolver.ResolveSassLauncher(
            mockFileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory);

        // Assert
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void ResolveSassLauncher_WithInvalidPath_ShouldThrowException()
    {
        var platform = SassRuntimeResolver.GetCurrentPlatform();

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            SassRuntimeResolver.ResolveSassLauncher(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                platform));
    }

    [Fact]
    public void ResolveSassLauncher_WithMatchingPack_ShouldReturnPathFromPack()
    {
        // Arrange
        var platform = Platform.LinuxArm64;
        var packs = new[]
        {
            new SassRuntimePack("Scarlet.Sass.Runtime.linux-x64", "linux-x64", "/packs/linux-x64/runtimes"),
            new SassRuntimePack("Scarlet.Sass.Runtime.linux-arm64", "linux-arm64", "/packs/linux-arm64/runtimes")
        };
        var fileSystem = FileSystemWithSass("/packs/linux-arm64/runtimes", platform);

        // Act
        var result = SassRuntimeResolver.ResolveSassLauncher(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(SassRuntimeResolver.GetLauncherPath("/packs/linux-arm64/runtimes", platform), result);
    }

    [Fact]
    public void ResolveSassLaunchCommand_WithOfficialBundle_ShouldLaunchDartDirectly()
    {
        // Arrange
        var platform = Platform.WindowsX64;
        var runtimesPath = "/packs/win-x64/runtimes";
        var launcher = SassRuntimeResolver.GetLauncherPath(runtimesPath, platform);
        var bundleDirectory = Path.GetDirectoryName(launcher)!;
        var dart = Path.Combine(bundleDirectory, "src", "dart.exe");
        var snapshot = Path.Combine(bundleDirectory, "src", "sass.snapshot");
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(launcher, new MockFileData("sass"));
        fileSystem.AddFile(dart, new MockFileData("dart"));
        fileSystem.AddFile(snapshot, new MockFileData("snapshot"));

        // Act
        var result = SassRuntimeResolver.ResolveSassLaunchCommand(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: runtimesPath);

        // Assert
        Assert.Equal(launcher, result.DisplayPath);
        Assert.Equal(dart, result.FileName);
        Assert.Equal(new[] { snapshot }, result.Arguments);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ResolveSassLaunchCommand_WhenOfficialBundleIsIncomplete_ShouldLaunchWrapper(
        bool hasDart,
        bool hasSnapshot)
    {
        // Arrange
        var platform = Platform.WindowsX64;
        var runtimesPath = "/packs/win-x64/runtimes";
        var launcher = SassRuntimeResolver.GetLauncherPath(runtimesPath, platform);
        var bundleDirectory = Path.GetDirectoryName(launcher)!;
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(launcher, new MockFileData("sass"));

        if (hasDart)
        {
            fileSystem.AddFile(Path.Combine(bundleDirectory, "src", "dart.exe"), new MockFileData("dart"));
        }

        if (hasSnapshot)
        {
            fileSystem.AddFile(Path.Combine(bundleDirectory, "src", "sass.snapshot"), new MockFileData("snapshot"));
        }

        // Act
        var result = SassRuntimeResolver.ResolveSassLaunchCommand(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: runtimesPath);

        // Assert
        Assert.Equal(launcher, result.DisplayPath);
        Assert.Equal(launcher, result.FileName);
        Assert.Empty(result.Arguments);
    }

    [Fact]
    public void ResolveSassLauncher_WithExplicitDirectory_ShouldIgnorePacks()
    {
        // Arrange - an explicit SassRuntimeDirectory is a deliberate override
        var platform = Platform.LinuxArm64;
        var packs = new[] { new SassRuntimePack("pack", "linux-arm64", "/packs/linux-arm64/runtimes") };
        var fileSystem = FileSystemWithSass("/explicit", platform);

        // Act
        var result = SassRuntimeResolver.ResolveSassLauncher(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: "/explicit",
            runtimePacks: packs);

        // Assert
        Assert.Equal(SassRuntimeResolver.GetLauncherPath("/explicit", platform), result);
    }

    [Fact]
    public void ResolveSassLauncher_WithHigherPriorityPack_ShouldPreferIt()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new SassRuntimePack("Scarlet.Sass.Runtime.linux-x64", "linux-x64", "/packs/baseline/runtimes", "baseline"),
            new SassRuntimePack("Contoso.Sass.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100)
        };

        var fileSystem = FileSystemWithSass("/packs/baseline/runtimes", platform);
        fileSystem.AddFile(SassRuntimeResolver.GetLauncherPath("/packs/custom/runtimes", platform), new MockFileData("fake executable"));

        // Act
        var result = SassRuntimeResolver.ResolveSassLauncher(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(SassRuntimeResolver.GetLauncherPath("/packs/custom/runtimes", platform), result);
    }

    [Fact]
    public void ResolveSassLauncher_WithBestPackMissingItsBinary_ShouldFallBackToNextCandidate()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new SassRuntimePack("Contoso.Sass.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new SassRuntimePack("Scarlet.Sass.Runtime.linux-x64", "linux-x64", "/packs/baseline/runtimes", "baseline")
        };
        var fileSystem = FileSystemWithSass("/packs/baseline/runtimes", platform);

        // Act
        var result = SassRuntimeResolver.ResolveSassLauncher(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(SassRuntimeResolver.GetLauncherPath("/packs/baseline/runtimes", platform), result);
    }

    [Fact]
    public void ResolveSassLauncher_WithPackForAnotherHost_ShouldNameWhatIsInstalled()
    {
        // Arrange - the classic "wrong runtime package referenced" mistake
        var packs = new[] { new SassRuntimePack("Scarlet.Sass.Runtime.linux-x64", "linux-x64", "/packs/linux-x64/runtimes") };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            SassRuntimeResolver.ResolveSassLauncher(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.MacOsArm64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("Sass runtime package not found", exception.Message);
        Assert.Contains("osx-arm64", exception.Message);
        Assert.Contains("Scarlet.Sass.Runtime.darwin-arm64", exception.Message);
        Assert.Contains("Scarlet.Sass.Runtime.linux-x64 (linux-x64)", exception.Message);
    }

    [Fact]
    public void ResolveSassLauncher_WithMatchingPackButNoBinary_ShouldListSearchedLocations()
    {
        // Arrange
        var packs = new[] { new SassRuntimePack("Scarlet.Sass.Runtime.darwin-arm64", "osx-arm64", "/packs/osx-arm64/runtimes") };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            SassRuntimeResolver.ResolveSassLauncher(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.MacOsArm64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("Sass launcher not found at", exception.Message);
        Assert.Contains("Scarlet.Sass.Runtime.darwin-arm64 (osx-arm64)", exception.Message);
        Assert.Contains(SassRuntimeResolver.GetLauncherPath("/packs/osx-arm64/runtimes", Platform.MacOsArm64), exception.Message);
    }

    [Fact]
    public void ResolveSassLauncher_WithMatchingPack_ShouldLogTheSelection()
    {
        // Arrange
        var platform = Platform.WindowsX64;
        var packs = new[] { new SassRuntimePack("Scarlet.Sass.Runtime.windows-x64", "win-x64", "/packs/win-x64/runtimes", "baseline") };
        var fileSystem = FileSystemWithSass("/packs/win-x64/runtimes", platform);
        var messages = new List<string>();

        // Act
        SassRuntimeResolver.ResolveSassLauncher(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs,
            log: messages.Add);

        // Assert
        Assert.Contains(messages, message => message.Contains("Scarlet.Sass.Runtime.windows-x64 (win-x64, baseline)"));
    }

    [Fact]
    public void SelectPacks_ShouldOrderByPriorityThenIdIndependentlyOfInputOrder()
    {
        // Arrange
        var low = new SassRuntimePack("z-pack", "linux-x64", "/z/runtimes");
        var alsoLow = new SassRuntimePack("a-pack", "linux-x64", "/a/runtimes");
        var high = new SassRuntimePack("m-pack", "linux-x64", "/m/runtimes", priority: 5);
        var other = new SassRuntimePack("other", "osx-arm64", "/o/runtimes", priority: 99);

        // Act
        var forward = SassRuntimeResolver.SelectPacks(new[] { low, alsoLow, high, other }, Platform.LinuxX64);
        var reversed = SassRuntimeResolver.SelectPacks(new[] { other, high, alsoLow, low }, Platform.LinuxX64);

        // Assert
        Assert.Equal(new[] { "m-pack", "a-pack", "z-pack" }, forward.Select(pack => pack.Id));
        Assert.Equal(forward.Select(pack => pack.Id), reversed.Select(pack => pack.Id));
    }

    [Fact]
    public void SelectPacks_WithSameIdAndPriority_ShouldBreakTheTieOnPath()
    {
        // Arrange - two packs indistinguishable except for where they live
        var second = new SassRuntimePack("same-id", "linux-x64", "/b/runtimes");
        var first = new SassRuntimePack("same-id", "linux-x64", "/a/runtimes");

        // Act
        var forward = SassRuntimeResolver.SelectPacks(new[] { second, first }, Platform.LinuxX64);
        var reversed = SassRuntimeResolver.SelectPacks(new[] { first, second }, Platform.LinuxX64);

        // Assert
        Assert.Equal(new[] { "/a/runtimes", "/b/runtimes" }, forward.Select(pack => pack.RuntimesPath));
        Assert.Equal(forward.Select(pack => pack.RuntimesPath), reversed.Select(pack => pack.RuntimesPath));
    }

    [Fact]
    public void ResolveSassLauncher_WithSeveralCandidates_ShouldLogHowManyWereConsidered()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new SassRuntimePack("Contoso.Sass.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new SassRuntimePack("Scarlet.Sass.Runtime.linux-x64", "linux-x64", "/packs/baseline/runtimes", "baseline")
        };
        var fileSystem = FileSystemWithSass("/packs/custom/runtimes", platform);
        var messages = new List<string>();

        // Act
        SassRuntimeResolver.ResolveSassLauncher(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs,
            log: messages.Add);

        // Assert
        Assert.Contains(messages, message =>
            message.Contains("Contoso.Sass.linux-x64") && message.Contains("out of 2 candidates"));
    }

    [Fact]
    public void ResolveSassLauncher_WithSeveralCandidatesAndNoBinary_ShouldListEveryLocation()
    {
        // Arrange
        var packs = new[]
        {
            new SassRuntimePack("Contoso.Sass.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new SassRuntimePack("Scarlet.Sass.Runtime.linux-x64", "linux-x64", "/packs/baseline/runtimes", "baseline")
        };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            SassRuntimeResolver.ResolveSassLauncher(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.LinuxX64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("2 runtime packs target linux-x64", exception.Message);
        Assert.Contains(SassRuntimeResolver.GetLauncherPath("/packs/custom/runtimes", Platform.LinuxX64), exception.Message);
        Assert.Contains(SassRuntimeResolver.GetLauncherPath("/packs/baseline/runtimes", Platform.LinuxX64), exception.Message);
    }

    [Fact]
    public void SelectPacks_WithNull_ShouldReturnEmpty()
    {
        // Act & Assert
        Assert.Empty(SassRuntimeResolver.SelectPacks(null, Platform.LinuxX64));
    }

    [Theory]
    [InlineData(Platform.WindowsArm64, "win-arm64", "sass.bat")]
    [InlineData(Platform.MacOsX64, "osx-x64", "sass")]
    public void GetLauncherPath_ShouldFollowTheRuntimePackLayout(Platform platform, string rid, string launcherName)
    {
        // Act
        var result = SassRuntimeResolver.GetLauncherPath("/packs/runtimes", platform);

        // Assert
        Assert.Equal(Path.GetFullPath(Path.Combine("/packs/runtimes", rid, "native", "dart-sass", launcherName)), result);
    }
}
