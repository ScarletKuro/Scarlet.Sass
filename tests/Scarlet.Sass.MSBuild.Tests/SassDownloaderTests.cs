using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.IO.Abstractions;
using System.Security.Cryptography;
using RichardSzalay.MockHttp;
using Scarlet.Sass.MSBuild.Tests.Mock;

namespace Scarlet.Sass.MSBuild.Tests;

public class SassDownloaderTests
{
    private const string ChecksumsUrlLatest = "https://github.com/oven-sh/Sass/releases/latest/download/SHASUMS256.txt";

    [Fact]
    public async Task DownloadRuntimeAsync_WithNullRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, SassRuntimeResolver.GetCurrentPlatform(), NoOpSassLogger.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync(null!));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithEmptyRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, SassRuntimeResolver.GetCurrentPlatform(), NoOpSassLogger.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync(string.Empty));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithWhitespaceRuntimeDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, SassRuntimeResolver.GetCurrentPlatform(), NoOpSassLogger.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadRuntimeAsync("   "));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithValidDirectory_ShouldReturnExecutablePath()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Sass executable
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
        Assert.Equal(
            new[] { mockFileSystem.Path.GetFullPath(result) },
            mockFileSystem.Directory.GetFiles(Path.Combine(tempDir, runtimeId, "native")));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithSpecificVersion_ShouldDownloadThatVersion()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Sass executable
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/download/Sass-v{version}/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
    }

    [Fact]
    public async Task DownloadRuntimeAsync_CalledTwice_ShouldReuseExistingRuntime()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;
        var executableName = SassRuntimeResolver.GetExecutableName(platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Track request count
        var requestCount = 0;

        // Create a mock zip file with the Sass executable
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/download/Sass-v{version}/Sass-linux-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(zipContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act - first download
        var result1 = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Verify file was created
        Assert.True(mockFileSystem.File.Exists(result1));

        // Act - second download (should reuse without downloading)
        var result2 = await downloader.DownloadRuntimeAsync(tempDir, version);

        // Assert
        Assert.Equal(result1, result2);

        // Verify HTTP was called only once (file was reused on second call)
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_ShouldKeepFinalExecutableHiddenUntilPublished()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var nativeDirectory = Path.Combine(tempDir, runtimeId, "native");
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var normalizedExpectedPath = mockFileSystem.Path.GetFullPath(expectedPath);
        var mockHttp = new MockHttpMessageHandler();
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var zipProvider = new ObservingZipArchiveProvider(
            mockFileSystem,
            expectedPath,
            stagedExecutablePath =>
            {
                Assert.True(mockFileSystem.File.Exists(stagedExecutablePath), "Staged executable should exist immediately after extraction");
                Assert.False(mockFileSystem.File.Exists(expectedPath), "Final executable should not be visible before publication");
            });
        var chmodProvider = new RecordingChmodProvider();
        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, zipProvider, chmodProvider, platform, NoOpSassLogger.Instance);

        var result = await downloader.DownloadRuntimeAsync(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(expectedPath));
        Assert.True(zipProvider.ObservedExtraction, "Expected extraction observation to run before publication");
        Assert.NotNull(zipProvider.StagedPath);
        Assert.Equal(zipProvider.StagedPath, chmodProvider.LastPath);
        Assert.NotEqual(expectedPath, chmodProvider.LastPath);
        Assert.Equal(
            new[] { normalizedExpectedPath },
            mockFileSystem.Directory.GetFiles(nativeDirectory).Select(mockFileSystem.Path.GetFullPath));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithInvalidVersion_ShouldThrowException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var invalidVersion = "invalid_version";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Mock a 404 response for invalid version
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/download/Sass-v{invalidVersion}/Sass-linux-x64.zip")
                .Respond(System.Net.HttpStatusCode.NotFound);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            downloader.DownloadRuntimeAsync(tempDir, invalidVersion));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenArchiveDoesNotContainExecutable_ShouldThrowInvalidDataException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Content is irrelevant for this test because the ZIP provider is faked.
        var zipContent = CreateMockSassZip("Sass");
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeMissingExecutableZipArchiveProvider(), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("did not contain expected executable", ex.Message);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenExtractionDoesNotCreateFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Content is irrelevant for this test because the ZIP provider is faked.
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeNoWriteZipArchiveProvider(), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("not found after extraction", ex.Message);
        Assert.Contains(expectedPath, ex.Message);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenPublicationFails_ShouldCleanUpStagedExecutable()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var nativeDirectory = Path.Combine(tempDir, runtimeId, "native");
        var expectedPath = Path.Combine(nativeDirectory, executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var chmodProvider = new ThrowingChmodProvider();
        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), chmodProvider, platform, NoOpSassLogger.Instance);

        await Assert.ThrowsAsync<IOException>(() => downloader.DownloadRuntimeAsync(tempDir));

        Assert.False(mockFileSystem.File.Exists(expectedPath));
        Assert.Empty(mockFileSystem.Directory.GetFiles(nativeDirectory));
    }

    [Fact]
    public void PublishStagedExecutable_WhenAnotherCallerPublishesBetweenDeleteAndMove_ShouldSwallowMoveFailureAndReturnFinalPath()
    {
        // Covers the concurrent-publish fallback in PublishStagedExecutable. The mutex in DownloadRuntime is
        // specifically designed to make this unreachable through the public API on a single machine (see its
        // own remarks) - PublishStagedExecutable's own stale-file check would just delete a merely
        // pre-existing final path before Move ever ran. To reach the fallback at all, the destination has to
        // reappear *between* that delete and the Move call, which RaceInjectingFileSystem simulates.
        var platform = Platform.LinuxX64;
        var stagedPath = "/test-runtime/linux-x64/native/.Sass.staged.tmp";
        var finalPath = "/test-runtime/linux-x64/native/Sass";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(stagedPath, new MockFileData("staged Sass executable"));
        // A stale file here is what makes PublishStagedExecutable call Delete(finalPath) at all - that
        // Delete is the hook RaceInjectingFileSystem uses to simulate the other caller's publish landing
        // in the gap right after it.
        mockFileSystem.AddFile(finalPath, new MockFileData("stale Sass executable"));

        var raceSimulatingFileSystem = new RaceInjectingFileSystem(mockFileSystem, finalPath, "published by another caller");
        var downloader = new SassDownloader(new HttpClient(), new FakeLatestVersionResolver(null), raceSimulatingFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.PublishStagedExecutable(stagedPath, finalPath);

        Assert.Equal(finalPath, result);
        Assert.Equal("published by another caller", mockFileSystem.File.ReadAllText(finalPath));
        Assert.False(mockFileSystem.File.Exists(stagedPath), "The staged file must still be cleaned up.");
    }

    [Fact]
    public void PublishStagedExecutable_WhenMoveSucceedsButDestinationStillMissing_ShouldThrowFileNotFoundException()
    {
        // Covers the final safety check in PublishStagedExecutable. File.Move is documented to either move
        // the file or throw - it never reports success while leaving the destination absent - but the mutex
        // in DownloadRuntime exists precisely because that guarantee doesn't hold across independent
        // processes/machines sharing a runtime directory. This simulates a broken Move to prove the check
        // actually catches such a violation rather than silently returning a path that doesn't exist.
        var platform = Platform.LinuxX64;
        var stagedPath = "/test-runtime/linux-x64/native/.Sass.staged.tmp";
        var finalPath = "/test-runtime/linux-x64/native/Sass";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(stagedPath, new MockFileData("staged Sass executable"));

        var noOpMoveFileSystem = new NoOpMoveFileSystem(mockFileSystem);
        var downloader = new SassDownloader(new HttpClient(), new FakeLatestVersionResolver(null), noOpMoveFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var ex = Assert.Throws<FileNotFoundException>(() =>
            downloader.PublishStagedExecutable(stagedPath, finalPath));

        Assert.Contains("not found after publication", ex.Message);
        Assert.Contains(finalPath, ex.Message);
    }

    [Fact]
    public void DownloadRuntime_WhenMutexAlreadyExists_ShouldLogWaitingAndResumedMessages()
    {
        // Covers the "another process is already downloading" logging branch. `createdNew` is false
        // whenever the named mutex object already exists, regardless of whether it is currently held -
        // which is exactly the situation described in DownloadRuntime's own doc comment: multiple MSBuild
        // projects targeting the same runtime directory in a monorepo build.
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mutexName = SassDownloader.CreateMutexName(expectedPath);
        using var preExistingMutex = new Mutex(false, mutexName, out _);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var logger = new RecordingSassLogger();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, logger);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.Contains(logger.Messages, m => m.Contains("Another process is downloading"));
        Assert.Contains(logger.Messages, m => m.Contains("Finished waiting"));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenPinnedVersionBumped_ShouldRedownloadAndUpdateMarker()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale Sass executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.3.12"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/download/Sass-v1.4.2/Sass-linux-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(zipContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion("1.4.2"), "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act - request a newer pinned version against a cache directory holding an older one
        var result = await downloader.DownloadRuntimeAsync(tempDir, "1.4.2");

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(markerPath).Trim());
        Assert.Equal("fake Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_LatestResolvesToUnchangedVersion_ShouldSkipReDownload()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("already-cached Sass executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.4.2"));

        // No .When(...) registered: if the code tried to download, the mock would throw.
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var resolver = new FakeLatestVersionResolver("1.4.2");
        var downloader = new SassDownloader(httpClient, resolver, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert - no download was attempted, so the cached executable is untouched
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, resolver.CallCount);
        Assert.Equal("already-cached Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "Sass.exe")]
    [InlineData(Platform.WindowsArm64, "Sass.exe")]
    [InlineData(Platform.LinuxX64, "Sass")]
    [InlineData(Platform.LinuxArm64, "Sass")]
    [InlineData(Platform.MacOsX64, "Sass")]
    [InlineData(Platform.MacOsArm64, "Sass")]
    public async Task DownloadRuntimeAsync_ForAllPlatforms_ShouldDownloadCorrectExecutable(Platform platform, string expectedExecutable)
    {
        // Arrange
        var tempDir = "/test-runtime";
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var downloadName = SassRuntimeResolver.GetDownloadName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        // Create a mock zip file with the Sass executable
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/latest/download/{downloadName}.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, $"{downloadName}.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result));
        Assert.EndsWith(expectedExecutable, result);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenChecksumDoesNotMatch_ShouldThrowInvalidDataException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var executableName = SassRuntimeResolver.GetExecutableName(platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        // Deliberately wrong checksum: a 64-hex-char value that does not match the actual archive.
        mockHttp.When(ChecksumsUrlLatest)
                .Respond("text/plain", $"{new string('0', 64)}  Sass-linux-x64.zip\n");

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("Checksum mismatch", ex.Message);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenChecksumsFileHasNoMatchingEntry_ShouldThrowInvalidDataException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var executableName = SassRuntimeResolver.GetExecutableName(platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        mockHttp.When(ChecksumsUrlLatest)
                .Respond("text/plain", $"{new string('a', 64)}  Sass-windows-x64.zip\n");

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("No checksum entry", ex.Message);
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WithRealWorldChecksumsFileFormat_ShouldVerifySuccessfully()
    {
        // Arrange - a real excerpt of oven-sh/Sass's SHASUMS256.txt captured from a live release, with only
        // the target entry's hash swapped for the mock archive's actual one. Every other line is untouched,
        // including several filenames sharing "Sass-linux-x64" as a prefix, so this guards against exact-match
        // regressions (e.g. an unescaped "." or a substring match) that a synthetic single-line fixture -
        // which is all the other tests here use - could never catch.
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var executableName = SassRuntimeResolver.GetExecutableName(platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        var actualHash = Convert.ToHexString(SHA256.HashData(zipContent.ToArray())).ToLowerInvariant();

        var realWorldChecksumsLines = new[]
        {
            "d9e0811fe1fe68cd7963c40710b3afaeae5bebfd3663714e94a659e72c53bce5  Sass-linux-x64-android-baseline-profile.zip",
            "fe36d8d4795e0eadc22fb6696d44d168491c2e5b9b7cbb12b8c96b0c0c40a4f9  Sass-linux-x64-android-baseline.zip",
            "c2a09028a8178246737f8ecb127aa6f9a789576162ea934e30b4b36a1aa387b8  Sass-linux-x64-android-profile.zip",
            "1e90fc0d3b83cb847b34338aff2765a7034bf53c50414344b3b0bf07a330a144  Sass-linux-x64-android.zip",
            "574be420a9e5212b082079a60af2f23f66b7d3f7713abb046d2f140ea63e5150  Sass-linux-x64-profile.zip",
            $"{actualHash}  Sass-linux-x64.zip",
            "f33acc7e775585218a300ae62377ab1dd02c5d624ff970fc9e0096505fd6e0c1  Sass-linux-x64-musl-profile.zip",
            "76e1db84e98f22f78de0a87e309bfbbf297732847f9720db36750646c85c8c18  Sass-linux-x64-musl.zip",
            "21754222f1aafea211c76dfb188044bc2010aec581a3f77c664f3b0b2d53b01a  Sass-linux-x64-musl-profile.zip",
            "4835eca59d6da70f4674f5642f6e459dcadab773695b2ed9922d131057989742  Sass-linux-x64-musl.zip",
            "fad558e312e123abc9abc6fcdc2370b60e8a9726575fc6bcc2fdc93e6b600a7f  Sass-linux-x64-profile.zip",
            "36368faef7527875d5ffa52e53cd48021741f2a83eb6208a8dd64068d422a913  Sass-linux-x64.zip",
        };
        var realWorldChecksums = string.Join("\n", realWorldChecksumsLines) + "\n";

        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        mockHttp.When(ChecksumsUrlLatest)
                .Respond("text/plain", realWorldChecksums);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act
        var result = await downloader.DownloadRuntimeAsync(tempDir);

        // Assert
        Assert.True(mockFileSystem.File.Exists(result));
    }

    [Fact]
    public async Task DownloadRuntimeAsync_WhenChecksumsDownloadFails_ShouldThrowInvalidDataException()
    {
        // Arrange
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var executableName = SassRuntimeResolver.GetExecutableName(platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        mockHttp.When(ChecksumsUrlLatest)
                .Respond(System.Net.HttpStatusCode.NotFound);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadRuntimeAsync(tempDir));

        Assert.Contains("Failed to download checksums", ex.Message);
    }

    #region DownloadRuntime (synchronous, mutex-protected)

    [Fact]
    public void DownloadRuntime_WithNullRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, SassRuntimeResolver.GetCurrentPlatform(), NoOpSassLogger.Instance);

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime(null!));
    }

    [Fact]
    public void DownloadRuntime_WithEmptyRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, SassRuntimeResolver.GetCurrentPlatform(), NoOpSassLogger.Instance);

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime(string.Empty));
    }

    [Fact]
    public void DownloadRuntime_WithWhitespaceRuntimeDirectory_ShouldThrowArgumentException()
    {
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var mockFileSystem = new MockFileSystem();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, SassRuntimeResolver.GetCurrentPlatform(), NoOpSassLogger.Instance);

        Assert.Throws<ArgumentException>(() =>
            downloader.DownloadRuntime("   "));
    }

    [Fact]
    public void DownloadRuntime_WithValidDirectory_ShouldReturnExecutablePath()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
        Assert.Equal(
            new[] { mockFileSystem.Path.GetFullPath(result) },
            mockFileSystem.Directory.GetFiles(Path.Combine(tempDir, runtimeId, "native")));
    }

    [Fact]
    public void DownloadRuntime_WithSpecificVersion_ShouldDownloadThatVersion()
    {
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/download/Sass-v{version}/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result), $"Expected executable to exist at {result}");
    }

    [Fact]
    public void DownloadRuntime_CalledTwice_ShouldReuseExistingRuntime()
    {
        var tempDir = "/test-runtime";
        var version = "1.3.12";
        var platform = Platform.LinuxX64;
        var executableName = SassRuntimeResolver.GetExecutableName(platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var requestCount = 0;

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/download/Sass-v{version}/Sass-linux-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(zipContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result1 = downloader.DownloadRuntime(tempDir, version);
        Assert.True(mockFileSystem.File.Exists(result1));

        var result2 = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(result1, result2);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public void DownloadRuntime_WhenPinnedVersionIsAlreadyCached_ShouldSkipDownload()
    {
        var tempDir = "/test-runtime";
        var version = "1.4.2";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("already-cached Sass executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData(version));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/download/Sass-v{version}/Sass-linux-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
                });

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(expectedPath, result);
        Assert.Equal(0, requestCount);
        Assert.Equal("already-cached Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_WhenPinnedVersionBumped_ShouldRedownloadAndUpdateMarker()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale Sass executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.3.12"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/download/Sass-v1.4.2/Sass-linux-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(zipContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion("1.4.2"), "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        // Request a newer pinned version against a cache directory holding an older one.
        var result = downloader.DownloadRuntime(tempDir, "1.4.2");

        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(markerPath).Trim());
        Assert.Equal("fake Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_WhenMarkerMissingButExecutableExists_ShouldRedownloadAndCreateMarker()
    {
        // Simulate a runtime cached by a pre-fix version of SassDownloader (executable present, no marker).
        var tempDir = "/test-runtime";
        var version = "1.4.2";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale Sass executable"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/download/Sass-v{version}/Sass-linux-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(zipContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion(version), "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir, version);

        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal(version, mockFileSystem.File.ReadAllText(markerPath).Trim());
        Assert.Equal("fake Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_LatestResolvesToUnchangedVersion_ShouldSkipReDownload()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("already-cached Sass executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.4.2"));

        // No .When(...) registered: if the code tried to download, the mock would throw.
        var mockHttp = new MockHttpMessageHandler();
        var httpClient = mockHttp.ToHttpClient();
        var resolver = new FakeLatestVersionResolver("1.4.2");
        var downloader = new SassDownloader(httpClient, resolver, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        // No download was attempted, so the cached executable is untouched.
        Assert.Equal(expectedPath, result);
        Assert.Equal(1, resolver.CallCount);
        Assert.Equal("already-cached Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_LatestResolvesToNewVersion_ShouldRedownloadAndUpdateMarker()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale Sass executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.4.1"));

        var mockHttp = new MockHttpMessageHandler();
        var requestCount = 0;
        var zipContent = CreateMockSassZip(executableName);
        // Resolved to 1.4.2, so the code downloads the tag-scoped URL directly, not /latest/download/.
        mockHttp.When("https://github.com/oven-sh/Sass/releases/download/Sass-v1.4.2/Sass-linux-x64.zip")
                .Respond(() =>
                {
                    requestCount++;
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(zipContent)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                    return Task.FromResult(response);
                });
        MockChecksums(mockHttp, ChecksumsUrlForVersion("1.4.2"), "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var resolver = new FakeLatestVersionResolver("1.4.2");
        var downloader = new SassDownloader(httpClient, resolver, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.Equal(1, requestCount);
        Assert.Equal("1.4.2", mockFileSystem.File.ReadAllText(markerPath).Trim());
        Assert.Equal("fake Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_LatestCannotResolveVersion_ShouldRedownloadAndClearStaleMarker()
    {
        // Simulate GitHub's redirect shape changing so the version can't be resolved.
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
        var markerPath = expectedPath + ".version";

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("stale Sass executable"));
        mockFileSystem.AddFile(markerPath, new MockFileData("1.4.1"));

        var mockHttp = new MockHttpMessageHandler();
        var zipContent = CreateMockSassZip(executableName);
        // Resolution failed, so the code falls back to the plain "latest" URL.
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var resolver = new FakeLatestVersionResolver(resolvedVersion: null);
        var downloader = new SassDownloader(httpClient, resolver, mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.False(mockFileSystem.File.Exists(markerPath), "Expected the stale marker to be cleared when the resolved version could not be determined");
        Assert.Equal("fake Sass executable", mockFileSystem.File.ReadAllText(expectedPath));
    }

    [Fact]
    public void DownloadRuntime_WhenArchiveDoesNotContainExecutable_ShouldThrowInvalidDataException()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip("Sass");
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeMissingExecutableZipArchiveProvider(), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var ex = Assert.Throws<InvalidDataException>(() =>
            downloader.DownloadRuntime(tempDir));

        Assert.Contains("did not contain expected executable", ex.Message);
    }

    [Fact]
    public void DownloadRuntime_WhenExtractionDoesNotCreateFile_ShouldThrowFileNotFoundException()
    {
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeNoWriteZipArchiveProvider(), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var ex = Assert.Throws<FileNotFoundException>(() =>
            downloader.DownloadRuntime(tempDir));

        Assert.Contains("not found after extraction", ex.Message);
        Assert.Contains(expectedPath, ex.Message);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "Sass.exe")]
    [InlineData(Platform.WindowsArm64, "Sass.exe")]
    [InlineData(Platform.LinuxX64, "Sass")]
    [InlineData(Platform.LinuxArm64, "Sass")]
    [InlineData(Platform.MacOsX64, "Sass")]
    [InlineData(Platform.MacOsArm64, "Sass")]
    public void DownloadRuntime_ForAllPlatforms_ShouldDownloadCorrectExecutable(Platform platform, string expectedExecutable)
    {
        var tempDir = "/test-runtime";
        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(platform);
        var executableName = SassRuntimeResolver.GetExecutableName(platform);
        var downloadName = SassRuntimeResolver.GetDownloadName(platform);
        var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When($"https://github.com/oven-sh/Sass/releases/latest/download/{downloadName}.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, $"{downloadName}.zip", zipContent);

        var httpClient = mockHttp.ToHttpClient();
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var result = downloader.DownloadRuntime(tempDir);

        Assert.Equal(expectedPath, result);
        Assert.True(mockFileSystem.File.Exists(result));
        Assert.EndsWith(expectedExecutable, result);
    }

    [Fact]
    public void DownloadRuntime_ReleasesMutexOnAcquiringThread_EvenWhenAwaitsResumeOnAnotherThread()
    {
        // Regression test: an earlier refactor made the mutex-guarded critical section a genuinely `async`
        // method, so a continuation resuming on a different pooled thread after an `await` made
        // Mutex.ReleaseMutex() throw ApplicationException - it is thread-affine, only the thread that
        // called WaitOne may release it. DownloadRuntime must stay fully synchronous end to end (blocking
        // via GetAwaiter().GetResult(), never `await`, inside the mutex try/finally) so the acquiring
        // thread never changes, no matter how its internal awaited HTTP calls get scheduled.
        var tempDir = "/test-runtime";
        var platform = Platform.LinuxX64;
        var executableName = SassRuntimeResolver.GetExecutableName(platform);

        var mockFileSystem = new MockFileSystem();
        var mockHttp = new MockHttpMessageHandler();

        var zipContent = CreateMockSassZip(executableName);
        mockHttp.When("https://github.com/oven-sh/Sass/releases/latest/download/Sass-linux-x64.zip")
                .Respond("application/zip", zipContent);
        MockChecksums(mockHttp, ChecksumsUrlLatest, "Sass-linux-x64.zip", zipContent);

        // Forces every awaited HTTP call to genuinely suspend rather than complete synchronously - only
        // then does `await` register a continuation with the ambient SynchronizationContext at all.
        var httpClient = new HttpClient(new AlwaysYieldsHandler(mockHttp));
        var downloader = new SassDownloader(httpClient, new FakeLatestVersionResolver(null), mockFileSystem, new FakeZipArchiveProvider(mockFileSystem), NoOpChmodProvider.Instance, platform, NoOpSassLogger.Instance);

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AlwaysResumesOnNewThreadSynchronizationContext());
        try
        {
            // If any await inside the download/verify/extract chain resumed inside the mutex's
            // try/finally instead of blocking it from the outside, this throws ApplicationException from
            // ReleaseMutex instead of returning normally.
            var result = downloader.DownloadRuntime(tempDir);

            Assert.True(mockFileSystem.File.Exists(result));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    #endregion

    private static string ChecksumsUrlForVersion(string version) =>
        $"https://github.com/oven-sh/Sass/releases/download/Sass-v{version}/SHASUMS256.txt";

    /// <summary>
    /// Registers a mock response for the SHASUMS256.txt endpoint that matches the real content of
    /// <paramref name="zipContent"/>, mirroring the "hash  filename" format Sass publishes per release.
    /// </summary>
    private static void MockChecksums(MockHttpMessageHandler mockHttp, string checksumsUrl, string archiveFileName, MemoryStream zipContent)
    {
        using var sha256 = SHA256.Create();
        var hash = Convert.ToHexString(sha256.ComputeHash(zipContent.ToArray())).ToLowerInvariant();
        mockHttp.When(checksumsUrl).Respond("text/plain", $"{hash}  {archiveFileName}\n");
    }

    private static MemoryStream CreateMockSassZip(string executableName)
    {
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Create entry with platform folder structure (e.g., "Sass-linux-x64/Sass")
            var entry = archive.CreateEntry($"Sass-linux-x64/{executableName}");
            using var entryStream = entry.Open();
            var content = "mock Sass executable"u8.ToArray();
            entryStream.Write(content, 0, content.Length);
        }
        memoryStream.Position = 0;
        return memoryStream;
    }

    private sealed class ObservingZipArchiveProvider : IZipArchiveProvider
    {
        private readonly FakeZipArchiveProvider _innerProvider;
        private readonly IFileSystem _fileSystem;
        private readonly string _finalExecutablePath;
        private readonly Action<string> _afterExtract;

        public ObservingZipArchiveProvider(IFileSystem fileSystem, string finalExecutablePath, Action<string> afterExtract)
        {
            _innerProvider = new FakeZipArchiveProvider(fileSystem);
            _fileSystem = fileSystem;
            _finalExecutablePath = finalExecutablePath;
            _afterExtract = afterExtract;
        }

        public string? StagedPath { get; private set; }

        public bool ObservedExtraction { get; private set; }

        public ZipArchive OpenRead(string archiveFileName)
        {
            return _innerProvider.OpenRead(archiveFileName);
        }

        public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
        {
            _innerProvider.ExtractToFile(source, destinationFileName, overwrite);

            StagedPath = destinationFileName;
            ObservedExtraction = true;
            Assert.True(_fileSystem.File.Exists(destinationFileName), "Expected staged executable to exist after extraction");
            Assert.False(_fileSystem.File.Exists(_finalExecutablePath), "Final executable should not exist during extraction");
            _afterExtract(destinationFileName);
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
    /// thread. A real thread pool only sometimes reuses a different thread after an await, which would make
    /// a test relying on it flaky; spawning a fresh OS thread every time makes the worst case deterministic.
    /// </summary>
    private sealed class AlwaysResumesOnNewThreadSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            new Thread(() => d(state)) { IsBackground = true }.Start();
        }
    }

    /// <summary>
    /// Wraps a <see cref="MockFileSystem"/>, swapping in <see cref="RaceInjectingFile"/> for
    /// <see cref="IFileSystem.File"/> and forwarding everything else untouched.
    /// </summary>
    private sealed class RaceInjectingFileSystem : IFileSystem
    {
        private readonly MockFileSystem _inner;

        public RaceInjectingFileSystem(MockFileSystem inner, string raceTargetPath, string raceContent)
        {
            _inner = inner;
            File = new RaceInjectingFile(inner, raceTargetPath, raceContent);
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
    /// Simulates another caller publishing <paramref name="_raceTargetPath"/> in the gap between
    /// <see cref="SassDownloader"/>'s own stale-file <c>Delete</c> and its subsequent <c>Move</c> - the one
    /// interleaving that makes <c>Move</c> throw <see cref="IOException"/> with the destination existing
    /// again, which no amount of pre-seeding state before the call can reach on its own.
    /// </summary>
    private sealed class RaceInjectingFile : MockFile
    {
        private readonly MockFileSystem _inner;
        private readonly string _raceTargetPath;
        private readonly string _raceContent;

        public RaceInjectingFile(MockFileSystem inner, string raceTargetPath, string raceContent) : base(inner)
        {
            _inner = inner;
            _raceTargetPath = raceTargetPath;
            _raceContent = raceContent;
        }

        public override void Delete(string path)
        {
            base.Delete(path);

            if (path == _raceTargetPath)
            {
                _inner.AddFile(_raceTargetPath, new MockFileData(_raceContent));
            }
        }
    }

    /// <summary>
    /// Wraps a <see cref="MockFileSystem"/>, swapping in <see cref="NoOpMoveFile"/> for
    /// <see cref="IFileSystem.File"/> and forwarding everything else untouched.
    /// </summary>
    private sealed class NoOpMoveFileSystem : IFileSystem
    {
        private readonly MockFileSystem _inner;

        public NoOpMoveFileSystem(MockFileSystem inner)
        {
            _inner = inner;
            File = new NoOpMoveFile(inner);
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
    /// Simulates a filesystem whose <c>Move</c> reports success without actually publishing the destination
    /// file - a contract violation a real <see cref="File.Move(string, string)"/> never commits, but one
    /// PublishStagedExecutable's own final existence check must still catch defensively.
    /// </summary>
    private sealed class NoOpMoveFile : MockFile
    {
        public NoOpMoveFile(MockFileSystem inner) : base(inner)
        {
        }

        public override void Move(string sourceFileName, string destFileName)
        {
            // Deliberately does not call base.Move and does not create destFileName.
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

    private sealed class ThrowingChmodProvider : IChmodProvider
    {
        public void EnsureExecutablePermissions(string filePath)
        {
            throw new IOException($"chmod failed for {filePath}");
        }
    }

    /// <summary>
    /// Stands in for <see cref="GitHubLatestVersionResolver"/> so tests can dictate what "latest" resolves
    /// to without touching the network. <see cref="GitHubLatestVersionResolver"/>'s own HTTP handling
    /// (redirect probe, Location header parsing) is covered separately in
    /// <c>GitHubLatestVersionResolverTests</c>.
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
