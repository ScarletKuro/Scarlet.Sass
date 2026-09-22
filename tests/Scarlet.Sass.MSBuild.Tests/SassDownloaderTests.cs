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
        var expectedPath = ExpectedExecutablePath(tempDir, platform);

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
        Assert.Equal("fake Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
        Assert.Equal(expectedPath, chmod.LastPath);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(expectedPath + ".version").Trim());
    }

    [Fact]
    public void DownloadRuntime_ForLinux_ShouldExtractARealTarGzArchive()
    {
        // Arrange - Linux/macOS never touch IZipArchiveProvider; they go through the hand-rolled tar/gzip
        // reader, so this exercises that path with a genuinely gzipped tar stream rather than a zip.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedExecutablePath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz")
                .Respond("application/gzip", CreateMockTarGz(("dart-sass/sass", "fake Sass executable")));

        var downloader = CreateDownloader(mockFileSystem, mockHttp, platform);

        // Act
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.Equal("fake Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
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
                    ("dart-sass/sass", "fake Sass executable"),
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
                .Respond("application/gzip", new MemoryStream(new byte[] { 1, 2, 3 }));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem),
            new FakeTarArchiveProvider(new[] { "../../evil.txt" }),
            NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);

        // Act & Assert
        var ex = Assert.Throws<InvalidDataException>(() => downloader.DownloadRuntime(tempDir, "1.4.2"));
        Assert.Contains("resolves outside the destination directory", ex.Message);
    }

    [Fact]
    public void DownloadRuntime_WhenTarArchiveDoesNotContainTheExecutable_ShouldThrowFileNotFoundException()
    {
        // Mirrors DownloadRuntime_WhenArchiveDoesNotContainTheExecutable_... for the zip path.
        var platform = Platform.LinuxX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz")
                .Respond("application/gzip", new MemoryStream(new byte[] { 1, 2, 3 }));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem),
            new FakeTarArchiveProvider(new[] { "dart-sass/README.md" }),
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
                .Respond("application/gzip", CreateMockTarGz(("dart-sass/sass", "fake Sass executable")));

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
                .Respond("application/zip", new MemoryStream(new byte[] { 1, 2, 3 }));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem, new[] { "../../evil.txt" }),
            new FakeTarArchiveProvider(),
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
        var expectedPath = ExpectedExecutablePath(tempDir, platform);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale executable"));
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
        Assert.Equal("fake Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_WhenMarkerMissingButExecutableExists_ShouldRedownloadAndCreateMarker()
    {
        // Simulate a runtime cached by a pre-marker version of SassDownloader (executable present, no marker).
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedExecutablePath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale executable"));

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
        var expectedPath = ExpectedExecutablePath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("already-cached executable"));
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
        Assert.Equal("already-cached executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_LatestResolvesToNewVersion_ShouldRedownloadAndUpdateMarker()
    {
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";
        var expectedPath = ExpectedExecutablePath(tempDir, platform);

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale executable"));
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
    public void DownloadRuntime_WhenArchiveDoesNotContainTheExecutable_ShouldThrowFileNotFoundException()
    {
        // FakeZipArchiveProvider.OpenRead always reads from its own in-memory bytes rather than the
        // downloaded file, so the missing-executable archive is injected through the provider itself.
        var platform = Platform.WindowsX64;
        var tempDir = "/test-runtime";

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-windows-x64.zip")
                .Respond("application/zip", new MemoryStream(new byte[] { 1, 2, 3 }));

        var downloader = new SassDownloader(
            mockHttp.ToHttpClient(),
            new FakeLatestVersionResolver(null),
            mockFileSystem,
            new FakeZipArchiveProvider(mockFileSystem, new[] { "dart-sass/not-sass.bat" }),
            new FakeTarArchiveProvider(),
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
        Platform platform, string downloadName, string extension, string executableName)
    {
        // Arrange
        var tempDir = "/test-runtime";
        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var url = $"{GithubReleasesUrl}/download/1.4.2/dart-sass-1.4.2-{downloadName}.{extension}";

        if (extension == "zip")
        {
            mockHttp.When(url).Respond("application/zip", CreateMockZip(executableName));
        }
        else
        {
            mockHttp.When(url).Respond("application/gzip", CreateMockTarGz(($"dart-sass/{executableName}", "fake Sass executable")));
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
            new FakeTarArchiveProvider(),
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
        var executablePath = ExpectedExecutablePath(tempDir, platform);

        using var mutexHeldSignal = new ManualResetEventSlim(false);
        using var releaseMutexSignal = new ManualResetEventSlim(false);

        var holderThread = new Thread(() =>
        {
            using var mutex = new Mutex(false, SassDownloader.CreateMutexName(executablePath), out _);
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
                new FakeTarArchiveProvider(),
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
            new FakeTarArchiveProvider(),
            chmodProvider ?? (IChmodProvider)NoOpChmodProvider.Instance,
            platform,
            NoOpSassLogger.Instance);
    }

    private static string ExpectedExecutablePath(string runtimeDirectory, Platform platform) =>
        SassRuntimeResolver.GetExecutablePath(runtimeDirectory, platform);

    private static Task<HttpResponseMessage> RespondWithZip(MemoryStream zipContent)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StreamContent(zipContent)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        return Task.FromResult(response);
    }

    private static MemoryStream CreateMockZip(string executableName) =>
        CreateMockZipWithEntry($"dart-sass/{executableName}", "fake Sass executable");

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
    /// Builds a real gzip-compressed tar archive so tests exercise <c>SassDownloader</c>'s own tar reader
    /// (used for every non-Windows platform) rather than the zip abstraction.
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

        public IReadOnlyList<string> Messages => _messages;

        public void LogMessage(string message) => _messages.Add(message);
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
