using System.IO.Abstractions.TestingHelpers;
using Scarlet.Sass.Cli.Tests.Mock;
using Scarlet.Sass.Cli.Watch;

namespace Scarlet.Sass.Cli.Tests;

public class SassCommandDispatcherTests
{
    [Fact]
    public void Run_WithWatchFirst_ShouldRunTheProjectAwareCommand()
    {
        var rawLauncher = new RecordingProcessLauncher();
        var watch = new RecordingWatchCommand();
        var dispatcher = new SassCommandDispatcher(CreateRawApplication(rawLauncher), watch, purePassthrough: false);

        var result = dispatcher.Run(["watch", "--project", "App.csproj"]);

        Assert.Equal(23, result);
        Assert.Equal(["--project", "App.csproj"], watch.Arguments!);
        Assert.Null(rawLauncher.Received);
    }

    [Fact]
    public void Run_WithPassthroughEnabled_ShouldForwardWatchToSass()
    {
        var rawLauncher = new RecordingProcessLauncher();
        var watch = new RecordingWatchCommand();
        var dispatcher = new SassCommandDispatcher(CreateRawApplication(rawLauncher), watch, purePassthrough: true);
        var arguments = new[] { "watch", "--project", "App.csproj" };

        dispatcher.Run(arguments);

        Assert.Equal(arguments, rawLauncher.ReceivedArguments);
        Assert.Null(watch.Arguments);
    }

    /// <summary>
    /// Only the bare word in first position is reserved; Dart Sass's own flag and a later "watch" (a file or
    /// directory with that name) still reach Dart Sass verbatim.
    /// </summary>
    [Theory]
    [InlineData("--watch", "in.scss:out.css")]
    [InlineData("in.scss", "watch")]
    [InlineData("Watch", "in.scss:out.css")]
    public void Run_WithoutTheBareWatchWordFirst_ShouldForwardToSass(string first, string second)
    {
        var rawLauncher = new RecordingProcessLauncher();
        var watch = new RecordingWatchCommand();
        var dispatcher = new SassCommandDispatcher(CreateRawApplication(rawLauncher), watch, purePassthrough: false);

        dispatcher.Run([first, second]);

        Assert.Equal([first, second], rawLauncher.ReceivedArguments);
        Assert.Null(watch.Arguments);
    }

    private static SassCliApplication CreateRawApplication(IProcessLauncher launcher)
    {
        const string toolDirectory = "/tool";
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(toolDirectory, "dart-sass", "sass"), new MockFileData("Sass"));
        var resolver = new SassCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            toolDirectory,
            (_, _) => throw new InvalidOperationException());

        return new SassCliApplication(
            resolver,
            launcher,
            SassCliOptions.FromEnvironment(
                new FakeEnvironmentProvider(new Dictionary<string, string>
                {
                    [SassCliOptions.CacheVariable] = "/cache"
                }),
                SassBuildInfo.PinnedSassVersion),
            new StringWriter(),
            new StringWriter());
    }

    private sealed class RecordingWatchCommand : ISassWatchCommand
    {
        public string[]? Arguments { get; private set; }

        public int Run(string[] args)
        {
            Arguments = args;
            return 23;
        }
    }
}
