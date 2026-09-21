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
    [InlineData(Platform.WindowsX64, "Sass-windows-x64")]
    [InlineData(Platform.WindowsArm64, "Sass-windows-arm64")]
    [InlineData(Platform.LinuxX64, "Sass-linux-x64")]
    [InlineData(Platform.LinuxArm64, "Sass-linux-arm64")]
    [InlineData(Platform.LinuxMuslX64, "Sass-linux-x64-musl")]
    [InlineData(Platform.LinuxMuslArm64, "Sass-linux-arm64-musl")]
    [InlineData(Platform.MacOsX64, "Sass-darwin-x64")]
    [InlineData(Platform.MacOsArm64, "Sass-darwin-arm64")]
    public void GetRuntimeDirectoryName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
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
    [InlineData(Platform.WindowsX64, "Sass.exe")]
    [InlineData(Platform.WindowsArm64, "Sass.exe")]
    [InlineData(Platform.LinuxX64, "Sass")]
    [InlineData(Platform.LinuxArm64, "Sass")]
    [InlineData(Platform.LinuxMuslX64, "Sass")]
    [InlineData(Platform.LinuxMuslArm64, "Sass")]
    [InlineData(Platform.MacOsX64, "Sass")]
    [InlineData(Platform.MacOsArm64, "Sass")]
    public void GetExecutableName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
        var result = SassRuntimeResolver.GetExecutableName(platform);

        // Assert
        Assert.Equal(expected, result);
    }
}
