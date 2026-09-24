using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Text;
using RichardSzalay.MockHttp;
using Scarlet.Sass.MSBuild.Tests.Mock;

namespace Scarlet.Sass.MSBuild.Tests;

/// <summary>
/// Covers <see cref="SassDownloader"/> against its real contract: Dart Sass releases are fetched from
/// <c>github.com/sass/dart-sass/releases/download/{version}/dart-sass-{version}-{platform}.{zip|tar.gz}</c>,
/// extracted directly into <c>{runtimeDirectory}/{rid}/native/dart-sass/</c> (no staged file, no checksum
/// manifest - Dart Sass does not publish one), and cached via a sibling <c>.version</c> marker file.
/// </summary>
public class SassDownloaderTests
{
    private const string GithubReleasesUrl = "https://github.com/sass/dart-sass/releases";

    [Fact]
    public async Task DownloadRuntimeAsync_WithNullRuntimeDirectory_ShouldThrowArgumentException()
    {
        var downloader = CreateDownloader(new MockFileSystem(), new MockHttpMessageHandler(), Platform.LinuxX64);

        await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadRuntimeAsync(null!));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithEmptyRuntimeDirectory_ShouldThrowArgumentException()
    {
        var downloader = CreateDownloader(new MockFileSystem(), new MockHttpMessageHandler(), Platform.LinuxX64);

        await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadRuntimeAsync(string.Empty));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithWhitespaceRuntimeDirectory_ShouldThrowArgumentException()
    {
        var downloader = CreateDownloader(new MockFileSystem(), new MockHttpMessageHandler(), Platform.LinuxX64);

        await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadRuntimeAsync("   "));
    }

    [Fact]
    public void DownloadRuntime_WithValidDirectory_ShouldExtractIntoTheDartSassLayoutAndSetPermissions()
    {
        // Arrange - Windows uses the .zip path through IZipArchiveProvider.
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedLauncherPath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond("application/zip", CreateMockZip("sass.bat"));

        var chmod = new RecordingChmodProvider();
        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform, chmodProvider: chmod);

        // Act
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(expectedPath));
        Assert.Equal("fake Sass launcher", mockFileSystem.File.ReadAllText(expectedPath));
        Assert.Equal(expectedPath, chmod.LastPath);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(expectedPath + ".version").Trim());
    }

    [Fact]
    public void DownloadRuntime_ForLinux_ShouldExtractARealTarGzArchive()
    {
        // Arrange - Linux/macOS never touch IZipArchiveProvider; they go through the tar/gzip reader, so
        // this exercises that path with a genuinely gzipped tar stream rather than a zip.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedLauncherPath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz")
                .Respond("application/gzip", CreateMockTarGz(("dart-sass/sass", "fake Sass launcher")));

        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform);

        // Act
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.Equal("fake Sass launcher", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_ForLinux_ShouldExtractNestedDirectoriesFromTarGz()
    {
        // Arrange - the real archive also carries "dart-sass/src/dart", the actual Dart runtime the "sass"
        // launcher script execs into. A tar reader that only handles flat entries would silently drop it.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz")
                .Respond("application/gzip", CreateMockTarGz(
                    ("dart-sass/sass", "fake Sass launcher"),
                    ("dart-sass/src/dart", "fake dart runtime")));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem),
            TarArchiveProvider.Instance,
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act
        downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert - GetFullPath matters here: on Windows, Path.Combine("/test-runtime", ...) alone stays a
        // drive-relative "\test-runtime\..." while the production code normalizes through
        // ResolveArchiveDestination's own Path.GetFullPath, which resolves "/test-runtime" against the
        // current drive. Skipping GetFullPath here made this test pass on Linux/macOS CI but fail on Windows.
        var dartPath = Path.GetFullPath(Path.Combine(tempDir, "linux-x64", "native", "dart-sass", "src", "dart"));
        Assert.Equal("fake dart runtime", mockFileSystem.File.ReadAllText(dartPath));
    }

    [Fact]
    public void DownloadRuntime_WithExplicitTarDirectoryEntry_ShouldCreateDirectory()
    {
        // Arrange - Dart Sass currently relies on nested file paths rather than explicit directory entries,
        // but directory headers are valid tar entries and SassDownloader owns the extraction behavior.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";
        var srcDirectory = Path.GetFullPath(Path.Combine(tempDir, "linux-x64", "native", "dart-sass", "src"));

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz")
                .Respond("application/gzip", new MemoryStream([1, 2, 3]));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem),
            new FakeTarArchiveProvider(new[]
            {
                FakeTarEntry.Directory("dart-sass/src/"),
                FakeTarEntry.File("dart-sass/sass", "fake Sass launcher")
            }),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act
        downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.True(mockFileSystem.Directory.Exists(srcDirectory));
    }

    [Fact]
    public void DownloadRuntime_WithTarEntryEscapingTheDestination_ShouldThrowInvalidDataException()
    {
        // Arrange - ResolveArchiveDestination's zip-slip guard is shared between the zip and tar extraction
        // paths; this proves it also applies when ITarArchiveProvider is what feeds it entry names, not just
        // the zip path exercised above. Mirrors DownloadRuntime_WithZipEntryEscapingTheDestination_...: the
        // malicious entry is injected through FakeTarArchiveProvider rather than the mocked HTTP response
        // body, since the real ITarArchiveProvider is swapped out entirely here.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz")
                .Respond("application/gzip", new MemoryStream([1, 2, 3]));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem),
            new FakeTarArchiveProvider([FakeTarEntry.File("../../evil.txt", "fake Sass launcher")]),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => downloader.DownloadRuntime(tempDir, "1.4.2"));
        Assert.Contains("resolves outside the destination directory", ex.Message);
    }

    [Fact]
    public void DownloadRuntime_WhenTarArchiveDoesNotContainTheLauncher_ShouldThrowFileNotFoundException()
    {
        // Mirrors DownloadRuntime_WhenArchiveDoesNotContainTheLauncher_... for the zip path.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz")
                .Respond("application/gzip", new MemoryStream([1, 2, 3]));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem),
            new FakeTarArchiveProvider([FakeTarEntry.File("dart-sass/README.md", "fake Sass launcher")]),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => downloader.DownloadRuntime(tempDir, "1.4.2"));
    }

    [Fact]
    public void DownloadRuntime_WhenStaleDartSassDirectoryExists_ShouldReplaceItRatherThanMerge()
    {
        // Arrange - a leftover file from a previous version must not survive alongside the new extraction.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";
        var staleFile = Path.Combine(tempDir, "linux-x64", "native", "dart-sass", "old-leftover.txt");

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(staleFile, new MockFileData("stale"));

        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz")
                .Respond("application/gzip", CreateMockTarGz(("dart-sass/sass", "fake Sass launcher")));

        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform);

        // Act
        downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.False(mockFileSystem.File.Exists(staleFile), "Expected the stale extraction directory to be removed before re-extracting.");
    }

    [Fact]
    public void DownloadRuntime_WithZipEntryEscapingTheDestination_ShouldThrowInvalidDataException()
    {
        // Arrange - guards ResolveArchiveDestination's zip-slip protection. FakeZipArchiveProvider.OpenRead
        // always reads from its own in-memory bytes rather than the downloaded file, so the malicious entry
        // has to be injected through the provider rather than the mocked HTTP response body.
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond("application/zip", new MemoryStream([1, 2, 3]));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem, ["../../evil.txt"]),
            DefaultTarArchiveProvider(),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => downloader.DownloadRuntime(tempDir, "1.4.2"));
        Assert.Contains("resolves outside the destination directory", ex.Message);
    }

    [Fact]
    public void DownloadRuntime_CalledTwiceForTheSamePinnedVersion_ShouldReuseTheCache()
    {
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    return RespondWithZip(CreateMockZip("sass.bat"));
                });

        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform);

        // Act
        var result1 = downloader.DownloadRuntime(tempDir, "1.4.2");
        var result2 = downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public void DownloadRuntime_WhenPinnedVersionBumped_ShouldRedownloadAndUpdateMarker()
    {
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedLauncherPath(tempDir, platform);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale launcher"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.3.0"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    return RespondWithZip(CreateMockZip("sass.bat"));
                });

        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform);

        // Act
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(markerPath).Trim());
        Assert.Equal("fake Sass launcher", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_WhenMarkerMissingButLauncherExists_ShouldRedownloadAndCreateMarker()
    {
        // Simulate a runtime cached by a pre-marker version of SassDownloader (launcher present, no marker).
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedLauncherPath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale launcher"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    return RespondWithZip(CreateMockZip("sass.bat"));
                });

        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform);

        // Act
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(expectedPath + ".version").Trim());
    }

    [Fact]
    public void DownloadRuntime_LatestResolvesToUnchangedVersion_ShouldSkipReDownload()
    {
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedLauncherPath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("already-cached launcher"));
        mockFileSystem.AddFile(expectedPath + ".version", new MockFileData("1.4.2"));

        // No .When(...) registered: if the code tried to download, the mock would throw.
        var mockHttp = new MockHttpMessageHandler();
        var resolver = new FakeLatestVersionResolver("1.4.2");
        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform, resolver);

        // Act
        var result = downloader.DownloadRuntime(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, resolver.CallCount);
        Assert.Equal("already-cached launcher", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_LatestResolvesToNewVersion_ShouldRedownloadAndUpdateMarker()
    {
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedLauncherPath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale launcher"));
        mockFileSystem.AddFile(expectedPath + ".version", new MockFileData("1.4.1"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    return RespondWithZip(CreateMockZip("sass.bat"));
                });

        var resolver = new FakeLatestVersionResolver("1.4.2");
        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform, resolver);

        // Act
        var result = downloader.DownloadRuntime(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(expectedPath + ".version").Trim());
    }

    [Fact]
    public void DownloadRuntime_LatestCannotBeResolved_ShouldThrowInvalidOperationException()
    {
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var resolver = new FakeLatestVersionResolver(resolvedVersion: null);
        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform, resolver);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => downloader.DownloadRuntime(tempDir));
        Assert.Contains("Could not resolve the latest Dart Sass release version", ex.Message);
    }

    [Fact]
    public async Task DownloadRuntime_WhenDownloadReturnsNotFound_ShouldThrowHttpRequestException()
    {
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/9.9.9/dart-sass-9.9.9-windows-x64.zip")
                .Respond(System.Net.HttpStatusCode.NotFound);

        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadRuntimeAsync(tempDir, "9.9.9"));
    }

    [Fact]
    public void DownloadRuntime_WhenArchiveDoesNotContainTheLauncher_ShouldThrowFileNotFoundException()
    {
        // FakeZipArchiveProvider.OpenRead always reads from its own in-memory bytes rather than the
        // downloaded file, so the missing-launcher archive is injected through the provider itself.
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond("application/zip", new MemoryStream([1, 2, 3]));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem, ["dart-sass/not-sass.bat"]),
            DefaultTarArchiveProvider(),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act & Assert
        var ex = Assert.Throws<FileNotFoundException>(() => downloader.DownloadRuntime(tempDir, "1.4.2"));
        Assert.Contains("was not found after extraction", ex.Message);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "windows-x64", "zip", "sass.bat")]
    [InlineData(Platform.WindowsArm64, "windows-arm64", "zip", "sass.bat")]
    [InlineData(Platform.LinuxX64, "linux-x64", "tar.gz", "sass")]
    [InlineData(Platform.LinuxArm64, "linux-arm64", "tar.gz", "sass")]
    [InlineData(Platform.MacOsX64, "macos-x64", "tar.gz", "sass")]
    [InlineData(Platform.MacOsArm64, "macos-arm64", "tar.gz", "sass")]
    [InlineData(Platform.LinuxMuslX64, "linux-x64-musl", "tar.gz", "sass")]
    [InlineData(Platform.LinuxMuslArm64, "linux-arm64-musl", "tar.gz", "sass")]
    public void DownloadRuntime_ForEveryPlatform_ShouldRequestTheCorrectArchiveNameAndExtension(
        Platform platform, string downloadName, string extension, string launcherName)
    {
        // Arrange
        var tempDir = "/test-runtime";
        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var url = $"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-{downloadName}.{extension}";

        if (extension == "zip")
        {
            mockHttp.When(url).Respond("application/zip", CreateMockZip(launcherName));
        }
        else
        {
            mockHttp.When(url).Respond("application/gzip", CreateMockTarGz(($"dart-sass/{launcherName}", "fake Sass launcher")));
        }

        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform);

        // Act
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert - a request to any other URL would leave MockHttpMessageHandler with nothing to respond
        // with, which surfaces as an exception before this assertion is reached.
        Assert.True(mockFileSystem.File.Exists(result));
    }

    [Fact]
    public void DownloadRuntime_ReleasesMutexOnAcquiringThread_EvenWhenAwaitsResumeOnAnotherThread()
    {
        // Regression test: DownloadRuntime's mutex-guarded critical section calls into async HTTP code via
        // GetAwaiter().GetResult() rather than `await`, so the thread that called Mutex.WaitOne is always
        // the one that reaches ReleaseMutex - required because Mutex is thread-affine and release from any
        // other thread throws ApplicationException. This forces every awaited HTTP call to genuinely
        // suspend and resume on a fresh thread to make sure that invariant actually holds end to end.
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond("application/zip", CreateMockZip("sass.bat"));

        var httpClient = new HttpClient(new AlwaysYieldsHandler(mockHttp));
        var downloader = new SassDownloader(
            httpClient,
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem),
            DefaultTarArchiveProvider(),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AlwaysResumesOnNewThreadSynchronizationContext());
        try
        {
            var result = downloader.DownloadRuntime(tempDir, "1.4.2");

            Assert.True(mockFileSystem.File.Exists(result));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Fact]
    public void DownloadRuntime_WhenMutexAlreadyHeld_ShouldLogWaitingMessage()
    {
        // A named Mutex is recursively reentrant for the thread that already owns it, so the "another
        // process holds it" branch can only be observed by holding the mutex from a genuinely different
        // thread - on the owning thread, WaitOne would succeed immediately instead of blocking.
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var launcherPath = ExpectedLauncherPath(tempDir, platform);

        using var mutexHeldSignal = new ManualResetEventSlim(false);
        using var releaseMutexSignal = new ManualResetEventSlim(false);

        var holderThread = new Thread(() =>
        {
            using var mutex = new Mutex(false, SassDownloader.CreateMutexName(launcherPath), out _);
            mutex.WaitOne();
            mutexHeldSignal.Set();
            releaseMutexSignal.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true
        };
        holderThread.Start();

        try
        {
            mutexHeldSignal.Wait();

            var mockFileSystem = new MockFileSystem();
            var mockHttp = new MockHttpMessageHandler();
            var logger = new RecordingSassLogger();
            var downloader = new SassDownloader(
                mockHttp.ToHttpClient(),
                new FakeLatestVersionResolver(null),
                mockFileSystem,
                new FakeZipArchiveProvider(mockFileSystem),
                DefaultTarArchiveProvider(),
                NoOpChmodProvider.Instance,
                platform,
                logger);

            // Act & Assert - the other holder never releases within the timeout.
            Assert.Throws<TimeoutException>(() => downloader.DownloadRuntime(tempDir, "1.4.2", mutexTimeoutSeconds: 0));
            Assert.Contains(logger.Messages, m => m.Contains("Another process is downloading", StringComparison.Ordinal));
        }
        finally
        {
            releaseMutexSignal.Set();
            holderThread.Join();
        }
    }

    [Fact]
    public async Task DownloadRuntime_WhenAnotherProcessPublishesWhileWaiting_ShouldSkipTheDownload()
    {
        // The re-check after acquiring the mutex is the entire reason the mutex exists, and nothing covered
        // it. Without it every process that queued behind the winner would re-download and re-extract on top
        // of the tree the others are already running from - and because extraction deletes dart-sass/ before
        // writing it back, that would take a good cache entry apart underneath them.
        var platform = Platform.WindowsX64;
        const string runtimeDirectory = "/test-runtime";
        var launcherPath = ExpectedLauncherPath(runtimeDirectory, platform);

        var fileSystem = new MockFileSystem();
        // No handler is registered, so the assertions below are backed up by the request failing outright if
        // the downloader ever decides to go to the network.
        using var mockHttp = new MockHttpMessageHandler();
        var logger = new RecordingSassLogger();
        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            fileSystem,
            new FakeZipArchiveProvider(fileSystem),
            DefaultTarArchiveProvider(),
            NoOpChmodProvider.Instance,
            platform,
            logger);

        using var mutexHeldSignal = new ManualResetEventSlim(false);
        using var publishSignal = new ManualResetEventSlim(false);

        var holderThread = new Thread(() =>
        {
            using var mutex = new Mutex(false, SassDownloader.CreateMutexName(launcherPath), out _);
            mutex.WaitOne();
            mutexHeldSignal.Set();
            publishSignal.Wait();

            // Publish the way a real download does, marker last, while still holding the mutex.
            fileSystem.AddFile(launcherPath, new MockFileData("sass"));
            fileSystem.AddFile(SassDownloader.GetVersionMarkerPath(launcherPath), new MockFileData("1.4.2"));

            mutex.ReleaseMutex();
        })
        {
            IsBackground = true
        };
        holderThread.Start();

        try
        {
            mutexHeldSignal.Wait();

            var download = Task.Run(() => downloader.DownloadRuntime(runtimeDirectory, "1.4.2", mutexTimeoutSeconds: 60));

            // This message is logged after the pre-mutex cache check and before WaitOne, so seeing it means
            // the downloader looked at an empty cache and is now queued behind the holder. Publishing before
            // that point would exercise the pre-mutex check instead of the branch under test.
            Assert.True(
                SpinWait.SpinUntil(
                    () => logger.Messages.Any(m => m.Contains("Another process is downloading", StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(30)),
                "The downloader never reported that it was waiting for another process.");

            publishSignal.Set();

            // Act
            var result = await download;

            // Assert
            Assert.Equal(launcherPath, result);
            Assert.Contains(
                logger.Messages,
                m => m.Contains("was downloaded by another process while waiting", StringComparison.Ordinal));
        }
        finally
        {
            publishSignal.Set();
            holderThread.Join();
        }
    }

    [Fact]
    public void DownloadRuntime_WhenTempArchiveCannotBeDeleted_ShouldStillSucceed()
    {
        // The cleanup runs in a finally, so a throw there would replace whatever the download or extraction
        // had actually reported - and on this path there was no failure at all: the runtime is extracted and
        // usable, and only the scratch archive could not be removed. A scanner holding the file open long
        // enough is all it takes on Windows.
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedLauncherPath(tempDir, platform);

        var real = new MockFileSystem();
        var file = new RecordingFile(real, failDeletes: true);
        var fileSystem = new MockFileSystemWithFile(real, file);

        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond("application/zip", CreateMockZip("sass.bat"));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            fileSystem,
            new FakeZipArchiveProvider(real),
            DefaultTarArchiveProvider(),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(real.File.Exists(expectedPath), "The runtime should still be published when cleanup fails.");
        Assert.Equal("1.4.2", real.File.ReadAllText(expectedPath + ".version").Trim());

        // The delete must still be attempted - swallowing the error is the point, skipping the cleanup is not.
        Assert.Contains(file.Deleted, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void DownloadRuntime_ShouldDeleteTheTempArchiveWithoutProbingForItFirst()
    {
        // File.Delete does not throw when the file is missing, so an Exists guard would only defend against
        // the harmless outcome while doing nothing about the locked file that actually fails. This pins that
        // the guard stays gone: re-adding it is a silent no-op that makes the cleanup look safer than it is.
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";

        var real = new MockFileSystem();
        var file = new RecordingFile(real);
        var fileSystem = new MockFileSystemWithFile(real, file);

        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond("application/zip", CreateMockZip("sass.bat"));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            fileSystem,
            new FakeZipArchiveProvider(real),
            DefaultTarArchiveProvider(),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act
        downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.Contains(file.Deleted, path => path.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.DoesNotContain(file.ExistenceChecks, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    /// <summary>
    /// Wraps a <see cref="MockFileSystem"/>, swapping in a custom <see cref="IFile"/> and forwarding
    /// everything else untouched. Subclassing <see cref="MockFile"/> keeps the real behaviour for every
    /// member the test does not override, so a new call in the downloader cannot silently get a default.
    /// </summary>
    private sealed class MockFileSystemWithFile : IFileSystem
    {
        private readonly MockFileSystem _inner;

        public MockFileSystemWithFile(MockFileSystem inner, IFile file)
        {
            _inner = inner;
            File = file;
        }

        public IFile File { get; }

        public IDirectory Directory => _inner.Directory;
        public IDirectoryInfoFactory DirectoryInfo => _inner.DirectoryInfo;
        public IDriveInfoFactory DriveInfo => _inner.DriveInfo;
        public IFileInfoFactory FileInfo => _inner.FileInfo;
        public IFileStreamFactory FileStream => _inner.FileStream;
        public IFileSystemWatcherFactory FileSystemWatcher => _inner.FileSystemWatcher;
        public IFileVersionInfoFactory FileVersionInfo => _inner.FileVersionInfo;
        public IPath Path => _inner.Path;
    }

    /// <summary>
    /// Records the paths passed to <c>Delete</c> and <c>Exists</c>, and optionally fails every delete to
    /// stand in for a scanner holding a file open. Everything else behaves like the underlying mock.
    /// </summary>
    private sealed class RecordingFile : MockFile
    {
        private readonly bool _failDeletes;

        public RecordingFile(MockFileSystem inner, bool failDeletes = false) : base(inner)
        {
            _failDeletes = failDeletes;
        }

        public List<string> Deleted { get; } = new();

        public List<string> ExistenceChecks { get; } = new();

        public override void Delete(string path)
        {
            Deleted.Add(path);

            if (_failDeletes)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            base.Delete(path);
        }

        public override bool Exists(string? path)
        {
            if (path is not null)
            {
                ExistenceChecks.Add(path);
            }

            return base.Exists(path);
        }
    }

    private static SassDownloader CreateDownloader(
        MockFileSystem fileSystem,
        MockHttpMessageHandler mockHttp,
        Platform platform,
        FakeLatestVersionResolver? resolver = null,
        RecordingChmodProvider? chmodProvider = null)
    {
        return new SassDownloader(
            mockHttp.ToHttpClient(),
            resolver ?? new FakeLatestVersionResolver(null),
            fileSystem,
            new FakeZipArchiveProvider(fileSystem),
            DefaultTarArchiveProvider(),
            chmodProvider ?? (IChmodProvider)NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);
    }

    private static string ExpectedLauncherPath(string runtimeDirectory, Platform platform) =>
        SassRuntimeResolver.GetLauncherPath(runtimeDirectory, platform);

    private static FakeTarArchiveProvider DefaultTarArchiveProvider() =>
        new([FakeTarEntry.File("dart-sass/sass", "fake Sass launcher")]);

    private static Task<HttpResponseMessage> RespondWithZip(MemoryStream zipContent)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StreamContent(zipContent)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        return Task.FromResult(response);
    }

    private static MemoryStream CreateMockZip(string launcherName) =>
        CreateMockZipWithEntry($"dart-sass/{launcherName}", "fake Sass launcher");

    private static MemoryStream CreateMockZipWithEntry(string entryName, string content)
    {
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            entryStream.Write(bytes, 0, bytes.Length);
        }

        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Builds a real gzip-compressed tar archive so tests exercise <c>SassDownloader</c>'s tar reader (used
    /// for every non-Windows platform) rather than the zip abstraction.
    /// </summary>
    private static MemoryStream CreateMockTarGz(params (string Name, string Content)[] entries)
    {
        using var tar = new MemoryStream();

        foreach (var (name, content) in entries)
        {
            WriteTarEntry(tar, name, Encoding.UTF8.GetBytes(content));
        }

        // Two 512-byte zero blocks mark the end of a tar archive.
        tar.Write(new byte[1024], 0, 1024);
        tar.Position = 0;

        var gzip = new MemoryStream();
        using (var gzipStream = new GZipStream(gzip, CompressionLevel.Fastest, leaveOpen: true))
        {
            tar.CopyTo(gzipStream);
        }

        gzip.Position = 0;
        return gzip;
    }

    private static void WriteTarEntry(Stream stream, string name, byte[] content)
    {
        var header = new byte[512];
        var nameBytes = Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, header, Math.Min(nameBytes.Length, 100));

        var sizeOctal = Convert.ToString(content.Length, 8).PadLeft(11, '0') + "\0";
        var sizeBytes = Encoding.ASCII.GetBytes(sizeOctal);
        Array.Copy(sizeBytes, 0, header, 124, sizeBytes.Length);

        header[156] = (byte)'0'; // regular file typeflag
        header[257] = (byte)'u';
        header[258] = (byte)'s';
        header[259] = (byte)'t';
        header[260] = (byte)'a';
        header[261] = (byte)'r';

        for (var i = 148; i < 156; i++)
        {
            header[i] = (byte)' ';
        }

        var checksum = header.Sum(b => b);
        var checksumOctal = Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ";
        var checksumBytes = Encoding.ASCII.GetBytes(checksumOctal);
        Array.Copy(checksumBytes, 0, header, 148, checksumBytes.Length);

        stream.Write(header, 0, header.Length);
        stream.Write(content, 0, content.Length);

        var padding = (512 - (content.Length % 512)) % 512;
        if (padding > 0)
        {
            stream.Write(new byte[padding], 0, padding);
        }
    }

    /// <summary>
    /// Forces every request through this handler to suspend rather than complete synchronously, so an
    /// `await` on it actually registers a continuation with the ambient <see cref="SynchronizationContext"/>
    /// instead of continuing inline (which is what an already-completed <see cref="Task"/> does).
    /// </summary>
    private sealed class AlwaysYieldsHandler : DelegatingHandler
    {
        public AlwaysYieldsHandler(HttpMessageHandler inner) : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A <see cref="SynchronizationContext"/> that marshals every continuation onto a brand-new dedicated
    /// thread, making the worst-case thread-hop deterministic instead of relying on the thread pool
    /// sometimes reusing a different thread after an await.
    /// </summary>
    private sealed class AlwaysResumesOnNewThreadSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            new Thread(() => d(state)) { IsBackground = true }.Start();
        }
    }

    private sealed class RecordingSassLogger : ISassLogger
    {
        private readonly List<string> _messages = new();

        // Snapshotted under the lock: the contended-mutex tests read this from the test thread while the
        // downloader is still logging from another one.
        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }

        public void LogMessage(string message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }
        }
    }

    private sealed class RecordingChmodProvider : IChmodProvider
    {
        public string? LastPath { get; private set; }

        public void EnsureExecutablePermissions(string filePath)
        {
            LastPath = filePath;
        }
    }

    /// <summary>
    /// Stands in for <see cref="GitHubLatestVersionResolver"/> so tests can dictate what "latest" resolves
    /// to without touching the network. <see cref="GitHubLatestVersionResolver"/>'s own HTTP handling is
    /// covered separately in <c>GitHubLatestVersionResolverTests</c>.
    /// </summary>
    private sealed class FakeLatestVersionResolver : ILatestVersionResolver
    {
        private readonly string? _resolvedVersion;

        public FakeLatestVersionResolver(string? resolvedVersion)
        {
            _resolvedVersion = resolvedVersion;
        }

        public int CallCount { get; private set; }

        public Task<string?> TryResolveVersionAsync(string latestDownloadUrl, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_resolvedVersion);
        }
    }
}
