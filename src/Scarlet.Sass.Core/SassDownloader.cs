using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Scarlet.Sass.Core.Providers;

namespace Scarlet.Sass.Core;

/// <summary>
/// Handles downloading Dart Sass runtimes from GitHub releases.
/// </summary>
public sealed class SassDownloader
{
    private const string GithubReleasesUrl = "https://github.com/sass/dart-sass/releases";
    private const string ArchiveRootDirectory = "dart-sass";

    private readonly Platform _platform;
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;
    private readonly IChmodProvider _chmodProvider;
    private readonly IZipArchiveProvider _zipProvider;
    private readonly ITarArchiveProvider _tarProvider;
    private readonly ISassLogger _log;
    private readonly ILatestVersionResolver _latestVersionResolver;

    public SassDownloader(
        HttpClient httpClient,
        ILatestVersionResolver latestVersionResolver,
        IFileSystem fileSystem,
        IZipArchiveProvider zipProvider,
        ITarArchiveProvider tarProvider,
        IChmodProvider chmodProvider,
        Platform platform,
        ISassLogger log)
    {
        _platform = platform;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _zipProvider = zipProvider;
        _tarProvider = tarProvider;
        _chmodProvider = chmodProvider;
        _log = log;
        _latestVersionResolver = latestVersionResolver;
    }

    public string DownloadRuntime(string runtimeDirectory, string? version = null, int mutexTimeoutSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new ArgumentException("Runtime directory must be specified when using SassRuntimeDownload", nameof(runtimeDirectory));
        }

        var runtimeId = SassRuntimeResolver.GetRuntimeIdentifier(_platform);
        var launcherPath = SassRuntimeResolver.GetLauncherPath(runtimeDirectory, _platform);
        var versionMarkerPath = GetVersionMarkerPath(launcherPath);
        var hasExplicitVersion = !string.IsNullOrWhiteSpace(version);

        if (hasExplicitVersion && IsCacheValidForVersion(launcherPath, versionMarkerPath, version!))
        {
            _log.LogMessage($"Dart Sass {version} is already cached at {launcherPath}. Skipping download.");
            EnsureExecutablePermissions(launcherPath);
            return launcherPath;
        }

        var fullRuntimePath = Path.Combine(runtimeDirectory, runtimeId, "native");
        _fileSystem.Directory.CreateDirectory(fullRuntimePath);

        using var mutex = new Mutex(false, CreateMutexName(launcherPath), out var createdNew);

        if (!createdNew)
        {
            _log.LogMessage("Another process is downloading the Sass runtime. Waiting...");
        }

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(mutexTimeoutSeconds));
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            throw new TimeoutException("Timed out waiting for another process to finish downloading the Sass runtime.");
        }

        try
        {
            if (hasExplicitVersion && IsCacheValidForVersion(launcherPath, versionMarkerPath, version!))
            {
                _log.LogMessage($"Dart Sass {version} was downloaded by another process while waiting. Skipping download.");
                EnsureExecutablePermissions(launcherPath);
                return launcherPath;
            }

            return hasExplicitVersion
                ? DownloadVersion(fullRuntimePath, launcherPath, versionMarkerPath, version!)
                : ResolveAndDownloadLatest(fullRuntimePath, launcherPath, versionMarkerPath);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public Task<string> DownloadRuntimeAsync(string runtimeDirectory, string? version = null, int mutexTimeoutSeconds = 300)
        => Task.Run(() => DownloadRuntime(runtimeDirectory, version, mutexTimeoutSeconds));

    public static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Scarlet.Sass");
        return client;
    }

    internal static string CreateMutexName(string launcherPath)
    {
        var normalizedPath = Path.GetFullPath(launcherPath).ToUpperInvariant();
        var hashString = HashUtilities.ComputeSha256Hex(normalizedPath).ToUpperInvariant();
        return $"Global\\ScarletSass_{hashString}";
    }

    private string DownloadVersion(string fullRuntimePath, string launcherPath, string versionMarkerPath, string version)
    {
        var archiveName = GetArchiveName(version);
        var downloadUrl = $"{GithubReleasesUrl}/download/{version}/{archiveName}";
        DownloadAndExtractAsync(downloadUrl, fullRuntimePath, archiveName).GetAwaiter().GetResult();

        if (!_fileSystem.File.Exists(launcherPath))
        {
            throw new FileNotFoundException($"Dart Sass launcher was not found after extraction at expected path: {launcherPath}");
        }

        EnsureExecutablePermissions(launcherPath);
        WriteVersionMarker(versionMarkerPath, version);
        return launcherPath;
    }

    private string ResolveAndDownloadLatest(string fullRuntimePath, string launcherPath, string versionMarkerPath)
    {
        var resolvedVersion = _latestVersionResolver
            .TryResolveVersionAsync($"{GithubReleasesUrl}/latest")
            .GetAwaiter()
            .GetResult();

        if (resolvedVersion is not null && IsCacheValidForVersion(launcherPath, versionMarkerPath, resolvedVersion))
        {
            _log.LogMessage($"Dart Sass 'latest' still resolves to {resolvedVersion}, which is already cached at {launcherPath}. Skipping download.");
            EnsureExecutablePermissions(launcherPath);
            return launcherPath;
        }

        if (resolvedVersion is null)
        {
            throw new InvalidOperationException("Could not resolve the latest Dart Sass release version from GitHub.");
        }

        _log.LogMessage($"Dart Sass 'latest' resolved to {resolvedVersion}.");
        return DownloadVersion(fullRuntimePath, launcherPath, versionMarkerPath, resolvedVersion);
    }

    private string GetArchiveName(string version)
    {
        var platformName = SassRuntimeResolver.GetDownloadName(_platform);
        var extension = SassRuntimeResolver.GetArchiveExtension(_platform);
        return $"dart-sass-{version}-{platformName}.{extension}";
    }

    private void EnsureExecutablePermissions(string launcherPath)
    {
        SassRuntimeResolver.EnsureExecutablePermissions(_fileSystem, _chmodProvider, launcherPath, _platform);
    }

    private async Task DownloadAndExtractAsync(string downloadUrl, string fullRuntimePath, string archiveName)
    {
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccessOrThrow(response, downloadUrl);

        var tempPath = Path.Combine(Path.GetTempPath(), $"{archiveName}.{Guid.NewGuid():N}.tmp");
        _fileSystem.Directory.CreateDirectory(Path.GetTempPath());

        try
        {
            using (var stream = _fileSystem.File.Create(tempPath))
            {
                await response.Content.CopyToAsync(stream);
            }

            DeleteDirectoryIfExists(Path.Combine(fullRuntimePath, ArchiveRootDirectory));

            if (archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExtractZip(tempPath, fullRuntimePath);
            }
            else
            {
                ExtractTarGz(tempPath, fullRuntimePath);
            }
        }
        finally
        {
            DeleteFileIfExists(tempPath);
        }
    }

    private void ExtractZip(string archivePath, string destinationDirectory)
    {
        using var archive = _zipProvider.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destinationPath = ResolveArchiveDestination(destinationDirectory, entry.FullName);
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.Directory.CreateDirectory(directory);
            }

            _zipProvider.ExtractToFile(entry, destinationPath, overwrite: true);
        }
    }

    private void ExtractTarGz(string archivePath, string destinationDirectory)
    {
        using var file = _fileSystem.File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);

        _tarProvider.ReadEntries(gzip, (entry, entryStream) =>
        {
            var destinationPath = ResolveArchiveDestination(destinationDirectory, entry.Name);

            if (entry.IsDirectory)
            {
                _fileSystem.Directory.CreateDirectory(destinationPath);
                return;
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.Directory.CreateDirectory(directory);
            }

            using var output = _fileSystem.File.Create(destinationPath);
            entryStream.CopyTo(output);
        });
    }

    private static void EnsureSuccessOrThrow(HttpResponseMessage response, string downloadUrl)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to download Sass runtime from {downloadUrl}. Status: {response.StatusCode}");
        }
    }

    private string ResolveArchiveDestination(string destinationDirectory, string entryName)
    {
        var normalizedName = entryName.Replace('/', Path.DirectorySeparatorChar);
        var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedName));
        var destinationRoot = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Archive entry '{entryName}' resolves outside the destination directory.");
        }

        return destinationPath;
    }

    private static string GetVersionMarkerPath(string launcherPath) => launcherPath + ".version";

    private bool IsCacheValidForVersion(string launcherPath, string versionMarkerPath, string expectedVersion)
    {
        if (!_fileSystem.File.Exists(launcherPath) || !_fileSystem.File.Exists(versionMarkerPath))
        {
            return false;
        }

        var storedVersion = _fileSystem.File.ReadAllText(versionMarkerPath).Trim();
        return string.Equals(storedVersion, expectedVersion, StringComparison.Ordinal);
    }

    private void WriteVersionMarker(string versionMarkerPath, string version)
    {
        _fileSystem.File.WriteAllText(versionMarkerPath, version);
    }

    private void DeleteFileIfExists(string path)
    {
        if (_fileSystem.File.Exists(path))
        {
            _fileSystem.File.Delete(path);
        }
    }

    private void DeleteDirectoryIfExists(string path)
    {
        if (_fileSystem.Directory.Exists(path))
        {
            _fileSystem.Directory.Delete(path, recursive: true);
        }
    }
}
