using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Scarlet.Sass.Cli.Tests.Mock;

namespace Scarlet.Sass.Cli.Tests;

public class SassCliSmokeTests
{
    private const string ToolDirectory = "/tool";
    private const string CacheRoot = "/cache";

    public static TheoryData<string[]> Arguments => new()
    {
        Array.Empty<string>(),
        new[] { "--version" },
        new[] { "--watch", "Sass:wwwroot/css" },
        new[] { "--load-path", "node_modules", "app.scss:app.css" },
        new[] { "--pkg-importer=node", "pkg:@scope/theme/theme.scss:theme.css" },
        new[] { "a b" },
        new[] { string.Empty },
        new[] { "trailing\\" },
        new[] { "日本語" }
    };

    [Theory]
    [MemberData(nameof(Arguments))]
    public void Run_ForwardsEveryArgumentVerbatim(string[] args)
    {
        var launcher = new RecordingProcessLauncher(exitCode: 17);
        var application = CreateApplication(launcher, out var embeddedPath);

        var result = application.Run(args);

        Assert.Equal(17, result);
        Assert.Equal(args, launcher.ReceivedArguments);
        Assert.NotNull(launcher.Received);
        Assert.Equal(embeddedPath, launcher.Received!.Value.ExecutablePath);
    }

    [Fact]
    public void Run_WithInfoFlagAndJson_WritesDiagnosticsWithoutLaunchingSass()
    {
        var launcher = new RecordingProcessLauncher();
        var stdout = new StringWriter();
        var application = CreateApplication(launcher, out _, stdout: stdout);

        var result = application.Run(new[] { SassCliApplication.InfoFlag, "--json" });

        Assert.Equal(ExitCodes.Success, result);
        Assert.Null(launcher.Received);

        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("embedded", document.RootElement.GetProperty("source").GetString());
        Assert.Equal("SCARLET_SASS_PATH", nameof(SassCliOptions.PathVariable) == nameof(SassCliOptions.PathVariable) ? SassCliOptions.PathVariable : string.Empty);
    }

    [Fact]
    public void Run_WithPassthroughEnabled_ForwardsInfoFlag()
    {
        var launcher = new RecordingProcessLauncher();
        var application = CreateApplication(
            launcher,
            out _,
            variables: new Dictionary<string, string>
            {
                [SassCliOptions.CacheVariable] = CacheRoot,
                [SassCliOptions.PassthroughVariable] = "1"
            });

        var result = application.Run(new[] { SassCliApplication.InfoFlag, "extra" });

        Assert.Equal(ExitCodes.Success, result);
        Assert.Equal(new[] { SassCliApplication.InfoFlag, "extra" }, launcher.ReceivedArguments);
    }

    [Fact]
    public void Resolver_UsesExplicitPathBeforeEmbeddedPath()
    {
        var fileSystem = new MockFileSystem();
        var embeddedPath = EmbeddedPath();
        fileSystem.AddFile(embeddedPath, new MockFileData("embedded"));
        fileSystem.AddFile("/custom/sass", new MockFileData("explicit"));

        var chmod = new RecordingChmodProvider();
        var resolver = CreateResolver(fileSystem, chmod);
        var options = Options(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = CacheRoot,
            [SassCliOptions.PathVariable] = "/custom/sass"
        });

        var resolution = resolver.Resolve(options, allowDownload: true, new NoOpLogger());

        Assert.Equal(SassSource.Explicit, resolution.Source);
        Assert.Equal("/custom/sass", resolution.ExecutablePath);
        Assert.Equal(new[] { "/custom/sass" }, chmod.Paths);
    }

    [Fact]
    public void Resolver_UsesVersionScopedCacheBeforeDownload()
    {
        var fileSystem = new MockFileSystem();
        var options = Options();
        var cachedPath = SassRuntimeResolver.GetExecutablePath(options.RuntimeDirectory, Platform.LinuxX64);
        fileSystem.AddFile(cachedPath, new MockFileData("cached"));
        fileSystem.AddFile(DartPathForSass(cachedPath), new MockFileData("dart"));

        var chmod = new RecordingChmodProvider();
        var resolver = CreateResolver(fileSystem, chmod);

        var resolution = resolver.Resolve(options, allowDownload: true, new NoOpLogger());

        Assert.Equal(SassSource.Cache, resolution.Source);
        Assert.Equal(cachedPath, resolution.ExecutablePath);
        Assert.Equal(new[] { cachedPath, DartPathForSass(cachedPath) }, chmod.Paths);
    }

    [Fact]
    public void Resolver_UsesEmbeddedRuntime_ShouldMakeLauncherAndDartExecutable()
    {
        var fileSystem = new MockFileSystem();
        var embeddedPath = EmbeddedPath();
        fileSystem.AddFile(embeddedPath, new MockFileData("embedded"));
        fileSystem.AddFile(DartPathForSass(embeddedPath), new MockFileData("dart"));

        var chmod = new RecordingChmodProvider();
        var resolver = CreateResolver(fileSystem, chmod);

        var resolution = resolver.Resolve(Options(), allowDownload: true, new NoOpLogger());

        Assert.Equal(SassSource.Embedded, resolution.Source);
        Assert.Equal(embeddedPath, resolution.ExecutablePath);
        Assert.Equal(new[] { embeddedPath, DartPathForSass(embeddedPath) }, chmod.Paths);
    }

    [Fact]
    public void DiagnosticsReport_DescribesDartSassDownloadUrl()
    {
        var options = Options(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = CacheRoot,
            [SassCliOptions.VersionVariable] = "1.2.3"
        });
        var resolution = new SassResolution(
            null,
            SassSource.NotFound,
            Platform.LinuxX64,
            SassRuntimeResolver.GetRuntimeIdentifier(Platform.LinuxX64),
            "1.2.3",
            options.CacheRoot,
            options.RuntimeDirectory,
            EmbeddedPath(),
            "missing");

        var report = DiagnosticsReport.ToText(resolution, options);

        Assert.Contains("https://github.com/sass/dart-sass/releases/download/1.2.3/dart-sass-1.2.3-linux-x64.tar.gz", report);
    }

    private static SassCliApplication CreateApplication(
        IProcessLauncher launcher,
        out string embeddedPath,
        IDictionary<string, string>? variables = null,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        var fileSystem = new MockFileSystem();
        embeddedPath = EmbeddedPath();
        fileSystem.AddFile(embeddedPath, new MockFileData("embedded"));

        return new SassCliApplication(
            CreateResolver(fileSystem, new RecordingChmodProvider()),
            launcher,
            Options(variables),
            stdout ?? new StringWriter(),
            stderr ?? new StringWriter());
    }

    private static SassCliResolver CreateResolver(MockFileSystem fileSystem, RecordingChmodProvider chmod)
    {
        return new SassCliResolver(
            fileSystem,
            chmod,
            Platform.LinuxX64,
            ToolDirectory,
            (_, _) => throw new InvalidOperationException("The downloader must not be used in this test."));
    }

    private static SassCliOptions Options(IDictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string> { [SassCliOptions.CacheVariable] = CacheRoot };

        return SassCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), SassBuildInfo.PinnedSassVersion);
    }

    private static string EmbeddedPath() => Path.Combine(ToolDirectory, "dart-sass", "sass");

    private static string DartPathForSass(string sassPath) => Path.Combine(Path.GetDirectoryName(sassPath)!, "src", "dart");

    private sealed class NoOpLogger : ISassLogger
    {
        public void LogMessage(string message)
        {
        }
    }
}
