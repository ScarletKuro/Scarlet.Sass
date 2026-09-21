using System.ComponentModel;
using System.IO.Abstractions.TestingHelpers;
using Scarlet.Sass.Cli.Tests.Mock;

namespace Scarlet.Sass.Cli.Tests;

/// <summary>
/// The paths taken when something goes wrong, plus the diagnostics line the CLI e2e depends on.
/// </summary>
public class SassCliApplicationTests
{
    private const string ToolDirectory = "/tool";

    [Fact]
    public void Run_WithAnExplicitPathThatDoesNotExist_ShouldReportItAndNotLaunch()
    {
        // Arrange - the one way resolution reports failure without throwing
        var launcher = new RecordingProcessLauncher();
        var stderr = new StringWriter();

        var application = Create(
            new MockFileSystem(),
            launcher,
            stderr: stderr,
            variables: new Dictionary<string, string>
            {
                [SassCliOptions.CacheVariable] = "/cache",
                [SassCliOptions.PathVariable] = "/nowhere/Sass"
            });

        // Act
        var result = application.Run(["--version"]);

        // Assert
        Assert.Equal(127, result);
        Assert.Null(launcher.Received);
        Assert.Contains(SassCliOptions.PathVariable, stderr.ToString());
        Assert.Contains("/nowhere/Sass", stderr.ToString());
    }

    [Fact]
    public void Run_WithDiagnosticsEnabled_ShouldReportTheResolvedSassOnStderr()
    {
        // Arrange - tests/e2e/cli-tool/verify.sh greps for this exact wording to prove which Sass ran
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/tool/Sass", new MockFileData("Sass"));

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var application = Create(
            fileSystem,
            new RecordingProcessLauncher(),
            stdout,
            stderr,
            new Dictionary<string, string>
            {
                [SassCliOptions.CacheVariable] = "/cache",
                [SassCliOptions.DiagnosticsVariable] = "1"
            });

        // Act
        application.Run(["--version"]);

        // Assert
        Assert.Contains("Scarlet.Sass: using Sass at ", stderr.ToString());
        Assert.Contains("Sass", stderr.ToString());

        // Nothing the tool says may reach stdout: `dotnet Sass ... | jq` has to keep working
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void Run_WithDiagnosticsDisabled_ShouldSayNothing()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/tool/Sass", new MockFileData("Sass"));

        var stderr = new StringWriter();
        var application = Create(fileSystem, new RecordingProcessLauncher(), stderr: stderr);

        // Act
        application.Run(["--version"]);

        // Assert
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void Run_WhenSassCannotBeStarted_ShouldReportItAsNotExecutable()
    {
        // Arrange - the file exists but the OS refuses to exec it: a missing exec bit, a corrupt download,
        // or a binary for the wrong architecture
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/tool/Sass", new MockFileData("Sass"));

        var stderr = new StringWriter();
        var application = Create(fileSystem, new ThrowingProcessLauncher(), stderr: stderr);

        // Act
        var result = application.Run(["--version"]);

        // Assert
        Assert.Equal(126, result);
        Assert.Contains("failed to start", stderr.ToString());
    }

    [Fact]
    public void Run_WhenTheDownloadThrows_ShouldReportItRatherThanCrash()
    {
        // Arrange
        var stderr = new StringWriter();

        var resolver = new SassCliResolver(
            new MockFileSystem(),
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            ToolDirectory,
            (_, _) => throw new InvalidOperationException("github is down"));

        var application = new SassCliApplication(
            resolver,
            new RecordingProcessLauncher(),
            SassCliOptions.FromEnvironment(
                new FakeEnvironmentProvider(new Dictionary<string, string> { [SassCliOptions.CacheVariable] = "/cache" }),
                SassBuildInfo.PinnedSassVersion),
            new StringWriter(),
            stderr);

        // Act
        var result = application.Run(["--version"]);

        // Assert
        Assert.Equal(127, result);
        Assert.Contains("could not obtain Sass", stderr.ToString());
        Assert.Contains("github is down", stderr.ToString());
    }

    private static SassCliApplication Create(
        MockFileSystem fileSystem,
        IProcessLauncher launcher,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        IDictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string> { [SassCliOptions.CacheVariable] = "/cache" };

        var resolver = new SassCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            ToolDirectory,
            (_, _) => throw new InvalidOperationException("The downloader must not be used in this test."));

        return new SassCliApplication(
            resolver,
            launcher,
            SassCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), SassBuildInfo.PinnedSassVersion),
            stdout ?? new StringWriter(),
            stderr ?? new StringWriter());
    }

    private sealed class ThrowingProcessLauncher : IProcessLauncher
    {
        public int Run(SassLaunchRequest request) => throw new Win32Exception(13, "Permission denied");
    }
}
