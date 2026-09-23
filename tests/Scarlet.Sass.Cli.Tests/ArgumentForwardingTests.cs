using System.IO.Abstractions.TestingHelpers;
using Scarlet.Sass.Cli.Tests.Mock;

namespace Scarlet.Sass.Cli.Tests;

/// <summary>
/// The contract that matters most: every argument reaches Sass unchanged, in order.
/// </summary>
/// <remarks>
/// <c>dotnet sass --version</c> has to print Sass's version, not the tool's, and a flag Sass adds tomorrow has
/// to work without a release of this package. Anything that parses, reorders, trims or re-quotes arguments
/// breaks that.
/// </remarks>
public class ArgumentForwardingTests
{
    public static TheoryData<string[]> Arguments => new()
    {
        Array.Empty<string>(),
        new[] { "--version" },
        new[] { "--help" },
        new[] { "input.scss", "output.css" },
        new[] { "--watch", "assets/styles:wwwroot/css" },
        new[] { "--style=compressed" },
        new[] { "--load-path", "node_modules", "site.scss", "site.css" },
        new[] { "--" },
        new[] { "--define", "X=\"y\"" },
        new[] { "a b" },
        new[] { string.Empty },
        new[] { "trailing\\" },
        new[] { "日本語" },
        new[] { "--flag=va lue", "second", string.Empty, "-" }
    };

    [Theory]
    [MemberData(nameof(Arguments))]
    public void Run_ShouldForwardEveryArgumentVerbatim(string[] args)
    {
        // Arrange
        var launcher = new RecordingProcessLauncher();
        var application = CreateApplication(launcher, out _);

        // Act
        application.Run(args);

        // Assert
        Assert.Equal(args, launcher.ReceivedArguments);
    }

    [Fact]
    public void Run_ShouldReturnSasssExitCodeUnchanged()
    {
        // Arrange - 130 is the shell's "terminated by SIGINT"; it must survive as-is
        foreach (var exitCode in new[] { 0, 1, 2, 3, 130, 255 })
        {
            var application = CreateApplication(new RecordingProcessLauncher(exitCode), out _);

            // Act
            var result = application.Run(new[] { "input.scss", "output.css" });

            // Assert
            Assert.Equal(exitCode, result);
        }
    }

    [Fact]
    public void Run_ShouldPassTheResolvedLaunchCommandToTheLauncher()
    {
        // Arrange
        var launcher = new RecordingProcessLauncher();
        var application = CreateApplication(launcher, out var embeddedPath);

        // Act
        application.Run(new[] { "--version" });

        // Assert
        Assert.NotNull(launcher.Received);
        Assert.EndsWith(Path.Combine("dart-sass", "src", "dart"), launcher.Received.Value.Command.FileName);
        Assert.EndsWith(Path.Combine("dart-sass", "src", "sass.snapshot"), Assert.Single(launcher.Received.Value.Command.Arguments));
        Assert.Equal(embeddedPath, launcher.Received.Value.Command.DisplayPath);
    }

    [Fact]
    public void Run_WhenNothingCanBeResolved_ShouldReportAndNotLaunch()
    {
        // Arrange - no embedded runtime, and a downloader that would fail the test if it were used
        var fileSystem = new MockFileSystem();
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = "/cache"
        });
        var launcher = new RecordingProcessLauncher();
        var stderr = new StringWriter();

        var resolver = new SassCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            "/tool",
            (_, _) => throw new InvalidOperationException("boom"));

        var application = new SassCliApplication(
            resolver,
            launcher,
            SassCliOptions.FromEnvironment(environment, SassBuildInfo.PinnedSassVersion),
            new StringWriter(),
            stderr);

        // Act
        var result = application.Run(new[] { "--version" });

        // Assert
        Assert.Equal(127, result);
        Assert.Null(launcher.Received);
        Assert.Contains("Scarlet.Sass:", stderr.ToString());
    }

    private static SassCliApplication CreateApplication(IProcessLauncher launcher, out string embeddedPath)
    {
        const string toolDirectory = "/tool";
        embeddedPath = Path.Combine(toolDirectory, "dart-sass", "sass");

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(embeddedPath, new MockFileData("fake Sass"));
        fileSystem.AddFile(Path.Combine(toolDirectory, "dart-sass", "src", "dart"), new MockFileData("fake Dart"));
        fileSystem.AddFile(Path.Combine(toolDirectory, "dart-sass", "src", "sass.snapshot"), new MockFileData("fake snapshot"));

        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = "/cache"
        });

        var resolver = new SassCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            toolDirectory,
            (_, _) => throw new InvalidOperationException("The embedded runtime must be used without downloading."));

        return new SassCliApplication(
            resolver,
            launcher,
            SassCliOptions.FromEnvironment(environment, SassBuildInfo.PinnedSassVersion),
            new StringWriter(),
            new StringWriter());
    }
}
