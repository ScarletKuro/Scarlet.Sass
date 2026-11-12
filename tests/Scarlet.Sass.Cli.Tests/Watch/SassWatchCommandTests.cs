using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Scarlet.Sass.Cli.Watch;

namespace Scarlet.Sass.Cli.Tests.Watch;

public class SassWatchCommandTests
{
    [Fact]
    public void Run_ShouldUseTheCurrentProjectAndDebugConfigurationByDefault()
    {
        var provider = new RecordingConfigurationProvider();
        var launcher = new RecordingWatchLauncher();
        var stderr = new StringWriter();
        var command = new SassWatchCommand(
            provider,
            launcher,
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo/app");

        var result = command.Run([]);

        Assert.Equal(0, result);
        Assert.Null(provider.Project);
        Assert.Equal("Debug", provider.Configuration);
        Assert.Equal("/repo/app", provider.CurrentDirectory);
        Assert.NotNull(launcher.Invocations);
        Assert.Equal(provider.Invocations, launcher.Invocations);
        Assert.Contains("watching 1 Sass input", stderr.ToString());
    }

    [Fact]
    public void Run_ShouldPassProjectAndConfigurationOptionsToMsBuildResolution()
    {
        var provider = new RecordingConfigurationProvider();
        var command = new SassWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            new StringWriter(),
            () => "/repo");

        var result = command.Run(["--project", "src/App.csproj", "-c", "Release"]);

        Assert.Equal(0, result);
        Assert.Equal("src/App.csproj", provider.Project);
        Assert.Equal("Release", provider.Configuration);
    }

    [Fact]
    public void Run_ShouldAcceptInlineLongOptions()
    {
        var provider = new RecordingConfigurationProvider();
        var command = new SassWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            new StringWriter(),
            () => "/repo");

        var result = command.Run(["--project=src/App.csproj", "--configuration=Release"]);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Equal("src/App.csproj", provider.Project);
        Assert.Equal("Release", provider.Configuration);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Run_WithHelp_ShouldPrintUsageWithoutResolvingTheProject(string option)
    {
        var provider = new RecordingConfigurationProvider();
        var stdout = new StringWriter();
        var command = new SassWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            stdout,
            new StringWriter(),
            () => "/repo");

        var result = command.Run([option]);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Null(provider.Configuration);
        Assert.Contains("Usage: dotnet sass watch [options]", stdout.ToString());
    }

    [Fact]
    public void Run_WithNoConfiguredEntries_ShouldNotStartAWatchProcess()
    {
        var provider = new RecordingConfigurationProvider { Invocations = [] };
        var launcher = new RecordingWatchLauncher();
        var stderr = new StringWriter();
        var command = new SassWatchCommand(
            provider,
            launcher,
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([]);

        Assert.Equal(ExitCodes.UsageError, result);
        Assert.Null(launcher.Invocations);
        Assert.Contains("no enabled", stderr.ToString());
    }

    [Theory]
    [InlineData("--project")]
    [InlineData("--configuration")]
    [InlineData("--unknown")]
    public void Run_WithAnInvalidOption_ShouldReturnAUsageError(string option)
    {
        var provider = new RecordingConfigurationProvider();
        var command = new SassWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            new StringWriter(),
            () => "/repo");

        var result = command.Run([option]);

        Assert.Equal(ExitCodes.UsageError, result);
        Assert.Null(provider.Configuration);
    }

    [Theory]
    [InlineData("--project=")]
    [InlineData("--configuration=   ")]
    public void Run_WithAnEmptyInlineOption_ShouldReturnAUsageError(string option)
    {
        var provider = new RecordingConfigurationProvider();
        var stderr = new StringWriter();
        var command = new SassWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([option]);

        Assert.Equal(ExitCodes.UsageError, result);
        Assert.Null(provider.Configuration);
        Assert.Contains("requires a non-empty value", stderr.ToString());
    }

    [Fact]
    public void Run_WhenConfigurationResolutionFails_ShouldReportTheWatchError()
    {
        var stderr = new StringWriter();
        var command = new SassWatchCommand(
            new ThrowingConfigurationProvider(
                new SassWatchException("could not evaluate the project", ExitCodes.SassNotFound)),
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([]);

        Assert.Equal(ExitCodes.SassNotFound, result);
        Assert.Contains("Scarlet.Sass: could not evaluate the project", stderr.ToString());
    }

    [Fact]
    public void Run_WhenAWatchProcessCannotStart_ShouldReportTheNativeError()
    {
        var stderr = new StringWriter();
        var command = new SassWatchCommand(
            new RecordingConfigurationProvider(),
            new ThrowingWatchLauncher(new Win32Exception(13, "Permission denied")),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([]);

        Assert.Equal(ExitCodes.SassNotExecutable, result);
        Assert.Contains("failed to start a watch process: Permission denied", stderr.ToString());
    }

    [Fact]
    public void Run_WhenAnOutputIsAlreadyWatched_ShouldExitWithoutStartingAnotherProcess()
    {
        var launcher = new RecordingWatchLauncher();
        var stderr = new StringWriter();
        var command = new SassWatchCommand(
            new RecordingConfigurationProvider(),
            launcher,
            new RecordingSessionLockProvider { AcquiredLock = null },
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([]);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Null(launcher.Invocations);
        Assert.Contains("already running", stderr.ToString());
    }

    /// <summary>
    /// The watcher must resolve exactly what the build resolves. A property override would let it write the
    /// same output file with different settings than the build that dotnet watch runs alongside it.
    /// </summary>
    [Theory]
    [InlineData("-p:SassOutputStyle=false")]
    [InlineData("--property:SassOutputStyle=false")]
    [InlineData("--property")]
    public void Run_WithAnMsBuildPropertyOverride_ShouldReturnAUsageError(string option)
    {
        var provider = new RecordingConfigurationProvider();
        var stderr = new StringWriter();
        var command = CreateCommand(provider, new RecordingWatchLauncher(), stderr);

        var result = command.Run([option]);

        Assert.Equal(ExitCodes.UsageError, result);
        Assert.Null(provider.Configuration);
        Assert.Contains("unrecognised watch option", stderr.ToString());
    }

    [Fact]
    public void Run_WithPoll_ShouldAppendItAfterTheWatchFlag()
    {
        var launcher = new RecordingWatchLauncher();
        var command = CreateCommand(new RecordingConfigurationProvider(), launcher);

        var result = command.Run(["--poll"]);

        Assert.Equal(ExitCodes.Success, result);
        var invocation = Assert.Single(launcher.Invocations!);
        Assert.Equal(
            "sass.snapshot --update Styles/app.scss:wwwroot/css/app.css --watch --poll",
            invocation.ProcessArguments);
    }

    /// <summary>
    /// Dart Sass's --poll takes no interval, so a value is a mistake rather than something to forward.
    /// </summary>
    [Theory]
    [InlineData("--poll=")]
    [InlineData("--poll=250")]
    public void Run_WithAPollValue_ShouldReturnAUsageError(string option)
    {
        var provider = new RecordingConfigurationProvider();
        var command = CreateCommand(provider, new RecordingWatchLauncher());

        var result = command.Run([option]);

        Assert.Equal(ExitCodes.UsageError, result);
        Assert.Null(provider.Configuration);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void Run_ShouldPollWhenTheDotnetPollingVariableIsSet(string? value, bool expectPoll)
    {
        var launcher = new RecordingWatchLauncher();
        var command = CreateCommand(
            new RecordingConfigurationProvider(),
            launcher,
            getEnvironmentVariable: name => name == SassWatchCommand.PollingFileWatcherVariable ? value : null);

        var result = command.Run([]);

        Assert.Equal(ExitCodes.Success, result);
        var invocation = Assert.Single(launcher.Invocations!);
        Assert.Equal(expectPoll, invocation.ProcessArguments.EndsWith(" --poll", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_WithPollAndThePollingVariable_ShouldAddPollOnce()
    {
        var launcher = new RecordingWatchLauncher();
        var command = CreateCommand(
            new RecordingConfigurationProvider(),
            launcher,
            getEnvironmentVariable: _ => "1");

        command.Run(["--poll"]);

        var invocation = Assert.Single(launcher.Invocations!);
        Assert.Single(invocation.ProcessArguments.Split(' '), argument => argument == "--poll");
    }

    [Fact]
    public void Run_ShouldListEveryInputOfEveryWatcher()
    {
        var provider = new RecordingConfigurationProvider
        {
            Invocations =
            [
                CreateInvocation(["Styles/a.scss", "Styles/b.scss"], ["wwwroot/a.css", "wwwroot/b.css"]),
                CreateInvocation(["Pages"], ["wwwroot/pages"])
            ]
        };
        var launcher = new RecordingWatchLauncher();
        var stderr = new StringWriter();
        var command = CreateCommand(provider, launcher, stderr);

        var result = command.Run([]);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Equal(2, launcher.Invocations!.Count());
        var output = stderr.ToString();
        Assert.Contains("watching 3 Sass input(s)", output);
        Assert.Contains("Styles/b.scss -> wwwroot/b.css", output);
        Assert.Contains("Pages -> wwwroot/pages", output);
    }

    private static SassWatchInvocation CreateInvocation(string[] inputs, string[] outputs)
    {
        return new SassWatchInvocation(
            "dart",
            "sass.snapshot --update "
                + string.Join(" ", inputs.Zip(outputs, static (input, output) => $"{input}:{output}"))
                + " --watch",
            "/repo",
            "sass",
            inputs,
            outputs,
            outputs);
    }

    private static SassWatchCommand CreateCommand(
        ISassWatchConfigurationProvider provider,
        IWatchProcessLauncher launcher,
        TextWriter? stderr = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        return new SassWatchCommand(
            provider,
            launcher,
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr ?? new StringWriter(),
            () => "/repo",
            getEnvironmentVariable);
    }

    private sealed class RecordingConfigurationProvider : ISassWatchConfigurationProvider
    {
        public IReadOnlyList<SassWatchInvocation> Invocations { get; set; } =
        [
            CreateInvocation(["Styles/app.scss"], ["wwwroot/css/app.css"])
        ];

        public string? Project { get; private set; }
        public string? Configuration { get; private set; }
        public string? CurrentDirectory { get; private set; }

        public IReadOnlyList<SassWatchInvocation> Resolve(
            string? project,
            string configuration,
            string currentDirectory)
        {
            Project = project;
            Configuration = configuration;
            CurrentDirectory = currentDirectory;
            return Invocations;
        }
    }

    private sealed class RecordingWatchLauncher : IWatchProcessLauncher
    {
        public IEnumerable<SassWatchInvocation>? Invocations { get; private set; }

        public int Run(IEnumerable<SassWatchInvocation> invocations)
        {
            Invocations = invocations;
            return 0;
        }
    }

    private sealed class ThrowingConfigurationProvider(SassWatchException exception)
        : ISassWatchConfigurationProvider
    {
        public IReadOnlyList<SassWatchInvocation> Resolve(
            string? project,
            string configuration,
            string currentDirectory) => throw exception;
    }

    private sealed class ThrowingWatchLauncher(Win32Exception exception) : IWatchProcessLauncher
    {
        public int Run(IEnumerable<SassWatchInvocation> invocations) => throw exception;
    }

    private sealed class RecordingSessionLockProvider : IWatchSessionLockProvider
    {
        public IDisposable? AcquiredLock { get; set; } = new NoopDisposable();

        public bool TryAcquire(
            IReadOnlyList<SassWatchInvocation> invocations,
            [NotNullWhen(true)] out IDisposable? lease)
        {
            lease = AcquiredLock;
            return lease is not null;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
