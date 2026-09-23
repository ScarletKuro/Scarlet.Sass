namespace Scarlet.Sass.Cli.Tests;

public class SassLaunchCommandTests
{
    [Fact]
    public void Constructor_WithValues_ShouldAssignProperties()
    {
        var arguments = new[] { "src/sass.snapshot" };

        var command = new SassLaunchCommand("dart", arguments, "sass");

        Assert.Equal("dart", command.FileName);
        Assert.Same(arguments, command.Arguments);
        Assert.Equal("sass", command.DisplayPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithMissingFileName_ShouldThrow(string? fileName)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SassLaunchCommand(fileName!, Array.Empty<string>(), "sass"));

        Assert.Equal("fileName", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullArguments_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new SassLaunchCommand("dart", null!, "sass"));

        Assert.Equal("arguments", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WithMissingDisplayPath_ShouldThrow(string? displayPath)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SassLaunchCommand("dart", Array.Empty<string>(), displayPath!));

        Assert.Equal("displayPath", exception.ParamName);
    }

    [Fact]
    public void FromLauncherPath_ShouldUseLauncherAsFileNameAndDisplayPath()
    {
        var command = SassLaunchCommand.FromLauncherPath("sass");

        Assert.Equal("sass", command.FileName);
        Assert.Empty(command.Arguments);
        Assert.Equal("sass", command.DisplayPath);
    }
}
