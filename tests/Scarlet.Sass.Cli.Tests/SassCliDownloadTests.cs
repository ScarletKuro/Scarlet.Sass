using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using RichardSzalay.MockHttp;
using Scarlet.Sass.Cli.Tests.Mock;

namespace Scarlet.Sass.Cli.Tests;

/// <summary>
/// Covers the resolver's download path, which is the portable <c>any</c> package's only route to a Sass.
/// </summary>
/// <remarks>
/// Driven through a real <c>SassDownloader</c> over a mocked transport rather than a stubbed one, because
/// what is worth checking is that the resolver hands it the right directory and version - and the
/// directory is version-scoped precisely so a later version request is not served the earlier binary.
/// </remarks>
public class SassCliDownloadTests
{
    // Rooted through GetFullPath so the paths are drive-qualified on Windows. A drive-less "/cache" is
    // ambiguous: MockFileSystem resolves it against its own default drive, while Path.GetFullPath - which
    // the resolver applies to the downloaded path - resolves it against the current directory's drive.
    // Those agree on a machine whose working directory is on C:, and disagree on CI, where the checkout
    // lives on D:.
    private static readonly string TestRoot = Path.GetFullPath("scarlet-Sass-download-tests");
    private static readonly string ToolDirectory = Path.Combine(TestRoot, "tool");
    private static readonly string CacheRoot = Path.Combine(TestRoot, "cache");

    // Platform.LinuxX64's archive name (SassRuntimeResolver.GetDownloadName) - checksum verification looks
    // up this exact filename in the mocked SHASUMS256.txt.
    private const string ArchiveFileName = "Sass-linux-x64.zip";
    private static readonly byte[] ArchiveBytes = [1, 2, 3];
    private static readonly string ArchiveSha256 = Convert.ToHexString(SHA256.HashData(ArchiveBytes)).ToLowerInvariant();

    [Fact]
    public void Resolve_WithNothingCached_ShouldDownloadAndReportItAsDownloaded()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        MockChecksums(handler, "https://github.com/oven-sh/Sass/releases/download/Sass-v1.4.2/SHASUMS256.txt");
        handler.When("https://github.com/oven-sh/Sass/releases/download/Sass-v1.4.2/*")
            .Respond("application/zip", new MemoryStream(ArchiveBytes));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.4.2");

        // Assert
        Assert.Equal(SassSource.Downloaded, resolution.Source);
        Assert.Equal(
            SassRuntimeResolver.GetExecutablePath(Path.Combine(CacheRoot, "runtimes", "1.4.2"), Platform.LinuxX64),
            resolution.ExecutablePath);
        Assert.True(fileSystem.File.Exists(resolution.ExecutablePath!));
    }

    [Fact]
    public void Resolve_ShouldRequestTheVersionScopedDirectory()
    {
        // Arrange - the CLI intentionally asks SassDownloader to use the cache directory scoped to the
        // requested version.
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        MockChecksums(handler, "https://github.com/oven-sh/Sass/releases/download/Sass-v1.3.6/SHASUMS256.txt");
        handler.When("https://github.com/oven-sh/Sass/releases/download/Sass-v1.3.6/*")
            .Respond("application/zip", new MemoryStream(ArchiveBytes));

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

        MockChecksums(handler, "https://github.com/oven-sh/Sass/releases/latest/download/SHASUMS256.txt");

        // Expect, not When: this asserts the URL shape rather than merely tolerating it
        handler.Expect("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
            .Respond("application/zip", new MemoryStream(ArchiveBytes));

        // Act
        var resolution = Resolve(fileSystem, handler, version: SassCliOptions.LatestVersion);

        // Assert
        handler.VerifyNoOutstandingExpectation();
        Assert.Equal(SassSource.Downloaded, resolution.Source);
    }

    private static void MockChecksums(MockHttpMessageHandler handler, string checksumsUrl)
    {
        handler.When(checksumsUrl).Respond("text/plain", $"{ArchiveSha256}  {ArchiveFileName}\n");
    }

    [Fact]
    public void Resolve_WhenTheDownloadFails_ShouldSurfaceTheFailure()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        handler.When("https://github.com/oven-sh/Sass/releases/download/*")
            .Respond(System.Net.HttpStatusCode.NotFound);

        // Act & Assert - the application turns this into exit code 127 with the message attached
        var exception = Assert.ThrowsAny<Exception>(() => Resolve(fileSystem, handler, version: "9.9.9"));
        Assert.Contains("9.9.9", exception.Message);
    }

    private static SassResolution Resolve(MockFileSystem fileSystem, MockHttpMessageHandler handler, string version)
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
            Platform.LinuxX64,
            ToolDirectory,
            (platform, log) => new SassDownloader(
                new HttpClient(handler),
                new FakeLatestVersionResolver(resolvedVersion: null),
                fileSystem,
                new FakeZipArchiveProvider(fileSystem),
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
