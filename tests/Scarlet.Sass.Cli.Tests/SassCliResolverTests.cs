using System.IO.Abstractions.TestingHelpers;
using Scarlet.Sass.Cli.Tests.Mock;

namespace Scarlet.Sass.Cli.Tests;

public class SassCliResolverTests
{
    private const string ToolDirectory = "/tool";
    private const string CacheRoot = "/cache";

    [Fact]
    public void Resolve_WithEmbeddedBinary_ShouldUseItWithoutDownloading()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var embedded = Path.Combine(ToolDirectory, "dart-sass", "sass");
        fileSystem.AddFile(embedded, new MockFileData("sass"));

        // Act
        var resolution = Resolve(fileSystem, out _);

        // Assert
        Assert.Equal(SassSource.Embedded, resolution.Source);
        Assert.Equal(embedded, resolution.LauncherPath);
    }

    [Fact]
    public void Resolve_WithOfficialEmbeddedBundle_ShouldLaunchDartDirectly()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var embedded = Path.Combine(ToolDirectory, "dart-sass", "sass");
        var dart = Path.Combine(ToolDirectory, "dart-sass", "src", "dart");
        var snapshot = Path.Combine(ToolDirectory, "dart-sass", "src", "sass.snapshot");
        fileSystem.AddFile(embedded, new MockFileData("sass"));
        fileSystem.AddFile(dart, new MockFileData("dart"));
        fileSystem.AddFile(snapshot, new MockFileData("snapshot"));

        // Act
        var resolution = Resolve(fileSystem, out _);

        // Assert
        Assert.Equal(embedded, resolution.LauncherPath);
        Assert.NotNull(resolution.LaunchCommand);
        Assert.EndsWith(Path.Combine("dart-sass", "src", "dart"), resolution.LaunchCommand!.FileName);
        Assert.EndsWith(Path.Combine("dart-sass", "src", "sass.snapshot"), Assert.Single(resolution.LaunchCommand.Arguments));
        Assert.Equal(embedded, resolution.LaunchCommand.DisplayPath);
    }

    [Fact]
    public void Resolve_WithEmbeddedBinary_ShouldMakeItExecutable()
    {
        // Arrange - NuGet packages carry no Unix permission bits, so without this the first run on Linux or
        // macOS fails with EACCES
        var fileSystem = new MockFileSystem();
        var embedded = Path.Combine(ToolDirectory, "dart-sass", "sass");
        fileSystem.AddFile(embedded, new MockFileData("sass"));

        // Act
        Resolve(fileSystem, out var chmod);

        // Assert
        Assert.Equal(new[] { embedded }, chmod.Paths);
    }

    [Fact]
    public void Resolve_WithEmbeddedBinaryAndNestedDartRuntime_ShouldMakeBothExecutable()
    {
        // Arrange - the "sass" launcher script execs into a nested "src/dart" binary, which needs its own
        // executable bit; only chmod'ing the launcher would still fail with EACCES on the very first run.
        var fileSystem = new MockFileSystem();
        var embedded = Path.Combine(ToolDirectory, "dart-sass", "sass");
        var dartRuntime = Path.Combine(Path.GetDirectoryName(embedded)!, "src", "dart");
        fileSystem.AddFile(embedded, new MockFileData("sass"));
        fileSystem.AddFile(dartRuntime, new MockFileData("dart"));

        // Act
        Resolve(fileSystem, out var chmod);

        // Assert
        Assert.Equal(new[] { embedded, dartRuntime }, chmod.Paths);
    }

    [Fact]
    public void Resolve_WithCachedBinary_ShouldUseItWithoutDownloading()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var options = CreateOptions();
        var cached = SassRuntimeResolver.GetLauncherPath(options.RuntimeDirectory, Platform.LinuxX64);
        fileSystem.AddFile(cached, new MockFileData("sass"));

        // Act
        var resolution = Resolve(fileSystem, out _, options);

        // Assert
        Assert.Equal(SassSource.Cache, resolution.Source);
        Assert.Equal(cached, resolution.LauncherPath);
    }

    [Fact]
    public void Resolve_WithExplicitPath_ShouldWinOverTheEmbeddedBinary()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(ToolDirectory, "dart-sass", "sass"), new MockFileData("embedded"));
        fileSystem.AddFile("/elsewhere/Sass", new MockFileData("explicit"));

        var options = CreateOptions(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = CacheRoot,
            [SassCliOptions.PathVariable] = "/elsewhere/Sass"
        });

        // Act
        var resolution = Resolve(fileSystem, out _, options);

        // Assert
        Assert.Equal(SassSource.Explicit, resolution.Source);
        Assert.Equal("/elsewhere/Sass", resolution.LauncherPath);
    }

    [Fact]
    public void Resolve_WithExplicitPathThatDoesNotExist_ShouldFailRatherThanFallBack()
    {
        // Arrange - an explicit instruction that silently did something else would be worse than an error
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(ToolDirectory, "dart-sass", "sass"), new MockFileData("embedded"));

        var options = CreateOptions(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = CacheRoot,
            [SassCliOptions.PathVariable] = "/missing/Sass"
        });

        // Act
        var resolution = Resolve(fileSystem, out _, options);

        // Assert
        Assert.False(resolution.IsResolved);
        Assert.Contains(SassCliOptions.PathVariable, resolution.FailureReason);
    }

    [Fact]
    public void Resolve_WithNoEmbeddedOptOut_ShouldSkipTheEmbeddedBinary()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(ToolDirectory, "dart-sass", "sass"), new MockFileData("embedded"));

        var options = CreateOptions(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = CacheRoot,
            [SassCliOptions.NoEmbeddedVariable] = "1"
        });

        // Act
        var resolution = Resolve(fileSystem, out _, options, allowDownload: false);

        // Assert
        Assert.NotEqual(SassSource.Embedded, resolution.Source);
    }

    [Fact]
    public void Resolve_WhenADifferentVersionIsRequested_ShouldNotUseTheEmbeddedBinary()
    {
        // Arrange - the embedded binary IS the pinned version, so honouring a different request means
        // bypassing it rather than silently ignoring the request
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(ToolDirectory, "dart-sass", "sass"), new MockFileData("embedded"));

        var options = CreateOptions(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = CacheRoot,
            [SassCliOptions.VersionVariable] = "1.0.0"
        });

        // Act
        var resolution = Resolve(fileSystem, out _, options, allowDownload: false);

        // Assert
        Assert.NotEqual(SassSource.Embedded, resolution.Source);
        Assert.Equal("1.0.0", resolution.RequestedVersion);
    }

    [Fact]
    public void Resolve_WithDownloadDisallowed_ShouldNeverConstructTheDownloader()
    {
        // Arrange - diagnostics must be able to report what would happen without fetching 90 MB
        var fileSystem = new MockFileSystem();

        // Act
        var resolution = Resolve(fileSystem, out _, allowDownload: false);

        // Assert
        Assert.False(resolution.IsResolved);
        Assert.Equal(SassSource.NotFound, resolution.Source);
    }

    [Fact]
    public void Resolve_ShouldScopeTheRuntimeDirectoryByVersion()
    {
        // Arrange - each requested CLI version gets a distinct cache directory so old and new tool versions
        // can coexist without sharing downloaded runtimes.
        var first = CreateOptions(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = CacheRoot,
            [SassCliOptions.VersionVariable] = "1.2.3"
        });
        var second = CreateOptions(new Dictionary<string, string>
        {
            [SassCliOptions.CacheVariable] = CacheRoot,
            [SassCliOptions.VersionVariable] = "4.5.6"
        });

        // Act & Assert
        Assert.NotEqual(first.RuntimeDirectory, second.RuntimeDirectory);
        Assert.Contains("1.2.3", first.RuntimeDirectory);
        Assert.Contains("4.5.6", second.RuntimeDirectory);
    }

    private static SassCliOptions CreateOptions(IDictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string> { [SassCliOptions.CacheVariable] = CacheRoot };

        return SassCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), SassBuildInfo.PinnedSassVersion);
    }

    private static SassResolution Resolve(
        MockFileSystem fileSystem,
        out RecordingChmodProvider chmod,
        SassCliOptions? options = null,
        bool allowDownload = true)
    {
        chmod = new RecordingChmodProvider();

        var resolver = new SassCliResolver(
            fileSystem,
            chmod,
            Platform.LinuxX64,
            ToolDirectory,
            (_, _) => throw new InvalidOperationException("The downloader must not be used in this test."));

        return resolver.Resolve(options ?? CreateOptions(), allowDownload, new NoOpLogger());
    }

    private sealed class NoOpLogger : ISassLogger
    {
        public void LogMessage(string message)
        {
        }
    }
}
