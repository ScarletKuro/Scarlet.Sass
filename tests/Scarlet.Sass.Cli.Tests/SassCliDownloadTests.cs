using System.IO.Abstractions.TestingHelpers;
using RichardSzalay.MockHttp;
using Scarlet.Sass.Cli.Tests.Mock;

namespace Scarlet.Sass.Cli.Tests;

/// <summary>
/// Covers the resolver's download path, which is the portable <c>any</c> package's only route to a Sass.
/// </summary>
/// <remarks>
/// Driven through a real <c>SassDownloader</c> over a mocked transport rather than a stubbed one, because
/// what is worth checking is that the resolver hands it the right directory and version - and the
/// directory is version-scoped precisely so a later version request is not served the earlier runtime.
/// Uses <see cref="Platform.WindowsX64"/> throughout so the archive goes through the mocked
/// <see cref="IZipArchiveProvider"/>; Dart Sass ships every other platform as a <c>.tar.gz</c>, which
/// <see cref="Scarlet.Sass.Core.SassDownloader"/> reads with its own tar/gzip code rather than that
/// abstraction, and is covered separately in <c>SassDownloaderTests</c>.
/// </remarks>
public class SassCliDownloadTests
{
    // Rooted through GetFullPath so the paths are drive-qualified on Windows. A drive-less "/cache" is
    // ambiguous: MockFileSystem resolves it against its own default drive, while Path.GetFullPath - which
    // the resolver applies to the downloaded path - resolves it against the current directory's drive.
    // Those agree on a machine whose working directory is on C:, and disagree on CI, where the checkout
    // lives on D:.
    private static readonly string TestRoot = Path.GetFullPath("scarlet-sass-download-tests");
    private static readonly string ToolDirectory = Path.Combine(TestRoot, "tool");
    private static readonly string CacheRoot = Path.Combine(TestRoot, "cache");

    private const string GithubReleasesUrl = "https://github.com/sass/dart-sass/releases";

    [Fact]
    public void Resolve_WithNothingCached_ShouldDownloadAndReportItAsDownloaded()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        handler.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
            .Respond("application/zip", new MemoryStream(new byte[] { 1, 2, 3 }));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.4.2");

        // Assert
        Assert.Equal(SassSource.Downloaded, resolution.Source);
        Assert.Equal(
            SassRuntimeResolver.GetLauncherPath(Path.Combine(CacheRoot, "runtimes", "1.4.2"), Platform.WindowsX64),
            resolution.LauncherPath);
        Assert.True(fileSystem.File.Exists(resolution.LauncherPath!));
    }

    [Fact]
    public void Resolve_WithCachedLauncherButNoVersionMarker_ShouldRedownloadInsteadOfUsingIt()
    {
        // Arrange - a download deletes dart-sass/, extracts, then writes the marker last, so a launcher with
        // no marker beside it is an extraction that was interrupted or is still running in another process,
        // and the tree can be missing the Dart VM the launcher execs into. The cache branch runs outside the
        // download mutex, so without the marker check it would hand back the half-written tree - and keep
        // doing so on every later run, since nothing there would ever repair it.
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        var cached = SassRuntimeResolver.GetLauncherPath(Path.Combine(CacheRoot, "runtimes", "1.4.2"), Platform.WindowsX64);
        fileSystem.AddFile(cached, new MockFileData("partially extracted"));

        handler.Expect($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
            .Respond("application/zip", new MemoryStream(new byte[] { 1, 2, 3 }));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.4.2");

        // Assert
        Assert.Equal(SassSource.Downloaded, resolution.Source);
        Assert.True(fileSystem.File.Exists(SassDownloader.GetVersionMarkerPath(resolution.LauncherPath!)));
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public void Resolve_ShouldRequestTheVersionScopedDirectory()
    {
        // Arrange - the CLI intentionally asks SassDownloader to use the cache directory scoped to the
        // requested version.
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        handler.When($"{GithubReleasesUrl}/download/1.3.6/dart-sass-1.3.6-windows-x64.zip")
            .Respond("application/zip", new MemoryStream(new byte[] { 1, 2, 3 }));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.3.6");

        // Assert
        Assert.Contains("1.3.6", resolution.RuntimeDirectory);
        Assert.DoesNotContain("1.4.2", resolution.RuntimeDirectory);
        Assert.Equal(SassSource.Downloaded, resolution.Source);
    }

    [Fact]
    public void Resolve_WithLatestRequested_ShouldAskForTheLatestRelease()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        // Expect, not When: this asserts the URL shape rather than merely tolerating it
        handler.Expect($"{GithubReleasesUrl}/download/1.5.0/dart-sass-1.5.0-windows-x64.zip")
            .Respond("application/zip", new MemoryStream(new byte[] { 1, 2, 3 }));

        // Act
        var resolution = Resolve(fileSystem, handler, version: SassCliOptions.LatestVersion, resolvedLatestVersion: "1.5.0");

        // Assert
        handler.VerifyNoOutstandingExpectation();
        Assert.Equal(SassSource.Downloaded, resolution.Source);
    }

    [Fact]
    public void Resolve_WhenTheDownloadFails_ShouldSurfaceTheFailure()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        handler.When($"{GithubReleasesUrl}/download/9.9.9/dart-sass-9.9.9-windows-x64.zip")
            .Respond(System.Net.HttpStatusCode.NotFound);

        // Act & Assert - the application turns this into exit code 127 with the message attached
        var exception = Assert.ThrowsAny<Exception>(() => Resolve(fileSystem, handler, version: "9.9.9"));
        Assert.Contains("9.9.9", exception.Message);
    }

    private static SassResolution Resolve(
        MockFileSystem fileSystem,
        MockHttpMessageHandler handler,
        string version,
        string? resolvedLatestVersion = null)
    {
        var options = SassCliOptions.FromEnvironment(
            new FakeEnvironmentProvider(new Dictionary<string, string>
            {
                [SassCliOptions.CacheVariable] = CacheRoot,
                [SassCliOptions.VersionVariable] = version
            }),
            SassBuildInfo.PinnedSassVersion);

        var resolver = new SassCliResolver(
            fileSystem,
            NoOpChmodProvider.Instance,
            Platform.WindowsX64,
            ToolDirectory,
            (platform, log) => new SassDownloader(
                new HttpClient(handler),
                new FakeLatestVersionResolver(resolvedLatestVersion),
                fileSystem,
                new FakeZipArchiveProvider(fileSystem),
                TarArchiveProvider.Instance,
                NoOpChmodProvider.Instance,
                platform,
                log));

        return resolver.Resolve(options, allowDownload: true, new RecordingSassLogger());
    }

    private sealed class RecordingSassLogger : ISassLogger
    {
        public void LogMessage(string message)
        {
        }
    }
}

