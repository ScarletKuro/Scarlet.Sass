namespace Scarlet.Sass.MSBuild.Tests;

public class PlatformTests
{
    [Fact]
    public void GetCurrentPlatform_ShouldReturnValidPlatform()
    {
        // Act
        var platform = SassRuntimeResolver.GetCurrentPlatform();

        // Assert
        Assert.True(Enum.IsDefined(platform));
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64")]
    [InlineData(Platform.WindowsArm64, "win-arm64")]
    [InlineData(Platform.LinuxX64, "linux-x64")]
    [InlineData(Platform.LinuxArm64, "linux-arm64")]
    [InlineData(Platform.LinuxMuslX64, "linux-musl-x64")]
    [InlineData(Platform.LinuxMuslArm64, "linux-musl-arm64")]
    [InlineData(Platform.MacOsX64, "osx-x64")]
    [InlineData(Platform.MacOsArm64, "osx-arm64")]
    public void GetRuntimeIdentifier_ShouldReturnCorrectIdentifier(Platform platform, string expected)
    {
        // Act
        var result = SassRuntimeResolver.GetRuntimeIdentifier(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRuntimeIdentifier_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            SassRuntimeResolver.GetRuntimeIdentifier(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "dart-sass")]
    [InlineData(Platform.WindowsArm64, "dart-sass")]
    [InlineData(Platform.LinuxX64, "dart-sass")]
    [InlineData(Platform.LinuxArm64, "dart-sass")]
    [InlineData(Platform.LinuxMuslX64, "dart-sass")]
    [InlineData(Platform.LinuxMuslArm64, "dart-sass")]
    [InlineData(Platform.MacOsX64, "dart-sass")]
    [InlineData(Platform.MacOsArm64, "dart-sass")]
    public void GetRuntimeDirectoryName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act - unlike the RID or download name, the extracted directory name is the same "dart-sass" on
        // every platform: it comes from the archive's own top-level folder, not from any RID convention.
        var result = SassRuntimeResolver.GetRuntimeDirectoryName(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRuntimeDirectoryName_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            SassRuntimeResolver.GetRuntimeDirectoryName(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "Scarlet.Sass.Runtime.windows-x64")]
    [InlineData(Platform.WindowsArm64, "Scarlet.Sass.Runtime.windows-arm64")]
    [InlineData(Platform.LinuxX64, "Scarlet.Sass.Runtime.linux-x64")]
    [InlineData(Platform.LinuxArm64, "Scarlet.Sass.Runtime.linux-arm64")]
    [InlineData(Platform.LinuxMuslX64, "Scarlet.Sass.Runtime.linux-x64-musl")]
    [InlineData(Platform.LinuxMuslArm64, "Scarlet.Sass.Runtime.linux-arm64-musl")]
    [InlineData(Platform.MacOsX64, "Scarlet.Sass.Runtime.darwin-x64")]
    [InlineData(Platform.MacOsArm64, "Scarlet.Sass.Runtime.darwin-arm64")]
    public void GetRuntimePackageName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
        var result = SassRuntimeResolver.GetRuntimePackageName(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRuntimePackageName_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            SassRuntimeResolver.GetRuntimePackageName(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "sass.bat")]
    [InlineData(Platform.WindowsArm64, "sass.bat")]
    [InlineData(Platform.LinuxX64, "sass")]
    [InlineData(Platform.LinuxArm64, "sass")]
    [InlineData(Platform.LinuxMuslX64, "sass")]
    [InlineData(Platform.LinuxMuslArm64, "sass")]
    [InlineData(Platform.MacOsX64, "sass")]
    [InlineData(Platform.MacOsArm64, "sass")]
    public void GetExecutableName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
        var result = SassRuntimeResolver.GetExecutableName(platform);

        // Assert
        Assert.Equal(expected, result);
    }
}
