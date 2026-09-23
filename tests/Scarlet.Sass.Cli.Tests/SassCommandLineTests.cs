namespace Scarlet.Sass.Cli.Tests;

public class SassCommandLineTests
{
    [Fact]
    public void BuildProcessArguments_WithLaunchAndSassArguments_ShouldPrependQuotedLaunchArguments()
    {
        var command = new SassLaunchCommand(
            @"C:\tools\dart-sass\src\dart.exe",
            new[] { @"C:\tools\dart-sass\src\sass.snapshot" },
            @"C:\tools\dart-sass\sass.bat");

        var result = SassCommandLine.BuildProcessArguments(command, @"--update ""input.scss:output.css""");

        Assert.Equal(@"""C:\\tools\\dart-sass\\src\\sass.snapshot"" --update ""input.scss:output.css""", result);
    }

    [Fact]
    public void FormatProcessCommand_WithProcessArguments_ShouldReturnQuotedFileNameAndArguments()
    {
        var command = new SassLaunchCommand(
            @"C:\tools\dart-sass\src\dart.exe",
            new[] { @"C:\tools\dart-sass\src\sass.snapshot" },
            @"C:\tools\dart-sass\sass.bat");

        var result = SassCommandLine.FormatProcessCommand(command, @"""C:\\tools\\dart-sass\\src\\sass.snapshot"" --version");

        Assert.Equal(@"""C:\\tools\\dart-sass\\src\\dart.exe"" ""C:\\tools\\dart-sass\\src\\sass.snapshot"" --version", result);
    }

    [Fact]
    public void FormatProcessCommand_WithNoArguments_ShouldReturnQuotedFileName()
    {
        var command = SassLaunchCommand.FromLauncherPath(@"C:\tools\dart-sass\sass.bat");

        var result = SassCommandLine.FormatProcessCommand(command, Array.Empty<string>());

        Assert.Equal(@"""C:\\tools\\dart-sass\\sass.bat""", result);
    }

    [Fact]
    public void FormatProcessCommand_WithUserArguments_ShouldPrependQuotedLaunchArguments()
    {
        var command = new SassLaunchCommand(
            @"C:\tools\dart-sass\src\dart.exe",
            new[] { @"C:\tools\dart-sass\src\sass.snapshot" },
            @"C:\tools\dart-sass\sass.bat");

        var result = SassCommandLine.FormatProcessCommand(command, new[] { "--version" });

        Assert.Equal(@"""C:\\tools\\dart-sass\\src\\dart.exe"" ""C:\\tools\\dart-sass\\src\\sass.snapshot"" ""--version""", result);
    }

    [Fact]
    public void QuoteArgument_ShouldEscapeBackslashesAndQuotes()
    {
        var result = SassCommandLine.QuoteArgument(@"C:\path with spaces\input ""main"".scss");

        Assert.Equal(@"""C:\\path with spaces\\input \""main\"".scss""", result);
    }
}
