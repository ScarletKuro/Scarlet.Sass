using System.IO.Abstractions;
using Scarlet.Sass.Core;
using Scarlet.Sass.Core.Providers;

namespace Scarlet.Sass.Cli;

/// <summary>
/// Resolves the Sass runtime the CLI should use.
/// </summary>
/// <remarks>
/// Precedence is explicit override, then the runtime embedded in this package, then the per-user download
/// cache, then a download. Every dependency is injected so the whole thing is testable with a
/// <c>MockFileSystem</c> and without a network.
/// </remarks>
internal sealed class SassCliResolver
{
    private readonly IFileSystem _fileSystem;
    private readonly IChmodProvider _chmodProvider;
    private readonly Platform _platform;
    private readonly string _baseDirectory;
    private readonly Func<Platform, ISassLogger, SassDownloader> _downloaderFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SassCliResolver"/> class.
    /// </summary>
    /// <param name="fileSystem">File system abstraction.</param>
    /// <param name="chmodProvider">Provider for setting executable permissions.</param>
    /// <param name="platform">The host platform.</param>
    /// <param name="baseDirectory">Directory the tool executable lives in; where an embedded Dart Sass runtime sits.</param>
    /// <param name="downloaderFactory">Creates the downloader, so tests can assert it is never invoked.</param>
    public SassCliResolver(
        IFileSystem fileSystem,
        IChmodProvider chmodProvider,
        Platform platform,
        string baseDirectory,
        Func<Platform, ISassLogger, SassDownloader> downloaderFactory)
    {
        _fileSystem = fileSystem;
        _chmodProvider = chmodProvider;
        _platform = platform;
        _baseDirectory = baseDirectory;
        _downloaderFactory = downloaderFactory;
    }

    /// <summary>
    /// Resolves the Sass launch command to run.
    /// </summary>
    /// <param name="options">Configuration read from the environment.</param>
    /// <param name="allowDownload">
    /// When <see langword="false"/> the network is never touched. Diagnostics use this so that asking the
    /// tool what it would do cannot itself trigger a 90 MB download.
    /// </param>
    /// <param name="log">Sink for download progress messages.</param>
    /// <returns>The resolution, which may be unsuccessful; only genuine download failures throw.</returns>
    public SassResolution Resolve(SassCliOptions options, bool allowDownload, ISassLogger log)
    {
        var runtimeIdentifier = SassRuntimeResolver.GetRuntimeIdentifier(_platform);
        var launcherName = SassRuntimeResolver.GetLauncherName(_platform);
        var embeddedPath = Path.Combine(_baseDirectory, "dart-sass", launcherName);

        SassResolution Build(SassLaunchCommand? command, SassSource source, string? failure = null) => new(
            command,
            source,
            _platform,
            runtimeIdentifier,
            options.RequestedVersion,
            options.CacheRoot,
            options.RuntimeDirectory,
            embeddedPath,
            failure);

        // 1. An explicit override is a deliberate instruction: honour it or fail, never silently fall back.
        if (!string.IsNullOrEmpty(options.ExplicitSassPath))
        {
            if (!_fileSystem.File.Exists(options.ExplicitSassPath))
            {
                return Build(
                    null,
                    SassSource.NotFound,
                    $"{SassCliOptions.PathVariable} points at '{options.ExplicitSassPath}', which does not exist.");
            }

            _chmodProvider.EnsureExecutablePermissions(options.ExplicitSassPath);

            return Build(SassRuntimeResolver.CreateLaunchCommand(_fileSystem, options.ExplicitSassPath, _platform), SassSource.Explicit);
        }

        // 2. The Dart Sass runtime shipped inside this package - the whole point of the RID-specific packages.
        if (CanUseEmbedded(options) && _fileSystem.File.Exists(embeddedPath))
        {
            // Mandatory, not defensive: NuGet packages carry no Unix permission bits, so on Linux and macOS
            // the embedded launcher and Dart VM are extracted 0644 and would fail with EACCES on first use.
            SassRuntimeResolver.EnsureExecutablePermissions(_fileSystem, _chmodProvider, embeddedPath, _platform);

            return Build(SassRuntimeResolver.CreateLaunchCommand(_fileSystem, embeddedPath, _platform), SassSource.Embedded);
        }

        // 3. A previous download. Checked before constructing a downloader so the happy path stays cheap,
        //    and so diagnostics can distinguish "cached" from "would download".
        //
        //    The version marker, not the launcher, is what proves the entry is usable. This branch runs
        //    outside the download mutex, and a download publishes a whole directory tree rather than a single
        //    file: it deletes dart-sass/, extracts, and writes the marker last. So a launcher without a
        //    marker means an extraction that was interrupted or is still in flight, and the Dart VM the
        //    launcher execs into may not be there yet. Taking it would exec a half-written tree - and because
        //    nothing here would ever repair it, every later run would do the same until the cache was cleared
        //    by hand. Falling through to the downloader re-downloads and fixes the entry instead.
        var cachedPath = SassRuntimeResolver.GetLauncherPath(options.RuntimeDirectory, _platform);
        if (_fileSystem.File.Exists(cachedPath) && _fileSystem.File.Exists(SassDownloader.GetVersionMarkerPath(cachedPath)))
        {
            SassRuntimeResolver.EnsureExecutablePermissions(_fileSystem, _chmodProvider, cachedPath, _platform);

            return Build(SassRuntimeResolver.CreateLaunchCommand(_fileSystem, cachedPath, _platform), SassSource.Cache);
        }

        if (!allowDownload)
        {
            return Build(null, SassSource.NotFound, "No Sass launcher is present yet; it would be downloaded on the next run.");
        }

        // 4. Download. SassDownloader handles cross-process races itself with a global mutex and the version
        //    marker above, so several tool invocations on a cold cache converge on one download.
        var downloader = _downloaderFactory(_platform, log);
        var downloadedPath = downloader.DownloadRuntime(
            options.RuntimeDirectory,
            options.DownloadVersion,
            options.DownloadTimeoutSeconds);

        // Normalised only so the reported path is stable. The cache branch above goes through
        // GetLauncherPath, which canonicalises; the downloader does not. Every default cache root is
        // absolute, so the two agree anyway - they diverge only when SCARLET_SASS_CACHE is set to a
        // relative path, and then --scarlet-info would report a relative path on the run that downloaded
        // and an absolute one on every run after. Nothing breaks either way: a relative path still
        // launches, because the working directory is inherited and never changed.
        return Build(
            SassRuntimeResolver.CreateLaunchCommand(_fileSystem, Path.GetFullPath(downloadedPath), _platform),
            SassSource.Downloaded);
    }

    private static bool CanUseEmbedded(SassCliOptions options)
    {
        if (options.IgnoreEmbedded)
        {
            return false;
        }

        // The embedded runtime *is* the pinned version. Asking for a different one has to bypass it, or the
        // request would be silently ignored.
        return string.Equals(options.RequestedVersion, SassBuildInfo.PinnedSassVersion, StringComparison.OrdinalIgnoreCase);
    }
}
