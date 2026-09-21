using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Scarlet.Sass.Core.Providers;

namespace Scarlet.Sass.Core;

/// <summary>
/// Helper class for detecting and resolving Sass runtime paths.
/// </summary>
public static class SassRuntimeResolver
{
    private static readonly IReadOnlyDictionary<Platform, PlatformInfo> PlatformMap =
        new Dictionary<Platform, PlatformInfo>
        {
            [Platform.WindowsX64] = new(
                rid: "win-x64",
                directoryName: "dart-sass",
                downloadName: "windows-x64",
                packageName: "Scarlet.Sass.Runtime.windows-x64",
                executableName: "sass.bat"
            ),
            [Platform.WindowsArm64] = new(
                rid: "win-arm64",
                directoryName: "dart-sass",
                downloadName: "windows-arm64",
                packageName: "Scarlet.Sass.Runtime.windows-arm64",
                executableName: "sass.bat"
            ),
            [Platform.LinuxX64] = new(
                rid: "linux-x64",
                directoryName: "dart-sass",
                downloadName: "linux-x64",
                packageName: "Scarlet.Sass.Runtime.linux-x64",
                executableName: "sass"
            ),
            [Platform.LinuxArm64] = new(
                rid: "linux-arm64",
                directoryName: "dart-sass",
                downloadName: "linux-arm64",
                packageName: "Scarlet.Sass.Runtime.linux-arm64",
                executableName: "sass"
            ),
            [Platform.MacOsX64] = new(
                rid: "osx-x64",
                directoryName: "dart-sass",
                downloadName: "macos-x64",
                packageName: "Scarlet.Sass.Runtime.darwin-x64",
                executableName: "sass"
            ),
            [Platform.MacOsArm64] = new(
                rid: "osx-arm64",
                directoryName: "dart-sass",
                downloadName: "macos-arm64",
                packageName: "Scarlet.Sass.Runtime.darwin-arm64",
                executableName: "sass"
            ),
            [Platform.LinuxMuslX64] = new(
                rid: "linux-musl-x64",
                directoryName: "dart-sass",
                downloadName: "linux-x64-musl",
                packageName: "Scarlet.Sass.Runtime.linux-x64-musl",
                executableName: "sass"
            ),
            [Platform.LinuxMuslArm64] = new(
                rid: "linux-musl-arm64",
                directoryName: "dart-sass",
                downloadName: "linux-arm64-musl",
                packageName: "Scarlet.Sass.Runtime.linux-arm64-musl",
                executableName: "sass"
            )
        };


    /// <summary>
    /// Gets the current platform.
    /// </summary>
    public static Platform GetCurrentPlatform()
    {
        var osPlatform = DetectOSPlatform(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

        return GetPlatform(
            osPlatform,
            RuntimeInformation.ProcessArchitecture,
            IsMuslLibc(),
            RuntimeInformation.OSDescription);
    }

    /// <summary>
    /// Maps the three OS checks .NET exposes to an <see cref="OSPlatform"/>.
    /// </summary>
    /// <returns><see langword="default"/> when none of the three match.</returns>
    /// <remarks>
    /// Split out from <see cref="GetCurrentPlatform"/> so the "none of the three matched" case can be
    /// tested: no real CI host can produce it, since every runner is Windows, Linux, or macOS.
    /// </remarks>
    internal static OSPlatform DetectOSPlatform(bool isWindows, bool isLinux, bool isOSX)
    {
        return isWindows ? OSPlatform.Windows
            : isLinux ? OSPlatform.Linux
            : isOSX ? OSPlatform.OSX
            : default;
    }

    /// <summary>
    /// Maps a host description to the Sass build that serves it.
    /// </summary>
    /// <param name="osPlatform">The host operating system.</param>
    /// <param name="architecture">The process architecture.</param>
    /// <param name="isMuslLibc">Whether the host uses musl rather than glibc.</param>
    /// <param name="osDescription">Description of the host, used only in error messages.</param>
    /// <returns>The platform to resolve a Sass build for.</returns>
    /// <exception cref="PlatformNotSupportedException">No Sass build covers this host.</exception>
    /// <remarks>
    /// Split out from <see cref="GetCurrentPlatform"/> so the unsupported combinations can be tested: they
    /// are the ones nobody can reproduce on a normal development machine, and they used to fail silently.
    /// </remarks>
    internal static Platform GetPlatform(
        OSPlatform osPlatform,
        Architecture architecture,
        bool isMuslLibc,
        string osDescription)
    {
        // x86 is folded into x64 rather than rejected. Sass ships no 32-bit build, but a 32-bit *host
        // process* on a 64-bit OS - an older MSBuild, for instance - can happily start the x64 binary,
        // and that has always worked.
        var isX64 = architecture is Architecture.X64 or Architecture.X86;
        var isArm64 = architecture == Architecture.Arm64;

        if (!isX64 && !isArm64)
        {
            throw new PlatformNotSupportedException(
                $"Sass does not publish a build for {architecture} ({osDescription}). "
                + "Supported architectures are x64 and arm64.");
        }

        if (osPlatform == OSPlatform.Windows)
        {
            return isArm64 ? Platform.WindowsArm64 : Platform.WindowsX64;
        }

        if (osPlatform == OSPlatform.Linux)
        {
            if (isMuslLibc)
            {
                return isArm64 ? Platform.LinuxMuslArm64 : Platform.LinuxMuslX64;
            }

            return isArm64 ? Platform.LinuxArm64 : Platform.LinuxX64;
        }

        if (osPlatform == OSPlatform.OSX)
        {
            return isArm64 ? Platform.MacOsArm64 : Platform.MacOsX64;
        }

        throw new PlatformNotSupportedException($"Unsupported platform: {osDescription}");
    }

    /// <summary>
    /// Directories the musl dynamic loader has been observed in, across the distributions that ship it.
    /// </summary>
    /// <remarks>
    /// Alpine (the only musl distribution covered by CI) always uses <c>/lib</c>. Other musl distros, such
    /// as Void Linux, install it under <c>/usr/lib</c> or <c>/lib64</c> instead; those are only a
    /// best-effort widening, not something a test host can verify, since no CI runner uses them.
    /// </remarks>
    private static readonly string[] MuslLoaderDirectories = { "/lib", "/lib64", "/usr/lib" };

    /// <summary>
    /// Detects a musl-based Linux distribution, such as Alpine.
    /// </summary>
    /// <returns><see langword="true"/> when the musl dynamic loader is present.</returns>
    /// <remarks>
    /// Probing for the loader keeps this working on netstandard2.0, where
    /// <c>RuntimeInformation.RuntimeIdentifier</c> is unavailable. A distro whose loader lives somewhere
    /// none of <see cref="MuslLoaderDirectories"/> covers falls through to the glibc build, which then
    /// fails to start with an ELF interpreter error rather than a clear "unsupported platform" message.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static bool IsMuslLibc()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

        try
        {
            foreach (var directory in MuslLoaderDirectories)
            {
                if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "ld-musl-*.so.1").Any())
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            // An unreadable directory is not a reason to fail; assume glibc and let the binary speak for itself.
            return false;
        }
    }

    /// <summary>
    /// Gets the runtime identifier (RID) for the specified platform.
    /// </summary>
    public static string GetRuntimeIdentifier(Platform platform) => GetInfo(platform).Rid;

    /// <summary>
    /// Gets the runtime directory name for the specified platform (for backwards compatibility).
    /// </summary>
    public static string GetRuntimeDirectoryName(Platform platform) => GetInfo(platform).DirectoryName;

    /// <summary>
    /// Gets the Sass executable name for the specified platform.
    /// </summary>
    public static string GetExecutableName(Platform platform) => GetInfo(platform).ExecutableName;

    /// <summary>
    /// Gets the runtime package name for the specified platform.
    /// </summary>
    public static string GetRuntimePackageName(Platform platform) => GetInfo(platform).PackageName;

    /// <summary>
    /// Gets the GitHub release download archive name for the specified platform.
    /// </summary>
    public static string GetDownloadName(Platform platform) => GetInfo(platform).DownloadName;

    /// <summary>
    /// Gets the full path the Sass executable is expected at inside a runtimes directory.
    /// </summary>
    /// <param name="runtimesPath">Directory containing <c>&lt;rid&gt;/native/&lt;executable&gt;</c>.</param>
    /// <param name="platform">The platform to build the path for.</param>
    /// <returns>The full path to the Sass executable. The file is not required to exist.</returns>
    public static string GetExecutablePath(string runtimesPath, Platform platform)
    {
        var info = GetInfo(platform);

        return Path.GetFullPath(Path.Combine(runtimesPath, info.Rid, "native", info.DirectoryName, info.ExecutableName));
    }

    /// <summary>
    /// Selects the runtime packs that can serve the given platform, best candidate first.
    /// </summary>
    /// <param name="packs">All packs contributed to the build. May be <see langword="null"/>.</param>
    /// <param name="platform">The platform that has to be served.</param>
    /// <returns>
    /// The matching packs ordered by descending <see cref="SassRuntimePack.Priority"/>, then by pack id and path so
    /// that the outcome does not depend on the order NuGet happened to import the runtime packages in.
    /// </returns>
    public static IReadOnlyList<SassRuntimePack> SelectPacks(IEnumerable<SassRuntimePack>? packs, Platform platform)
    {
        if (packs is null)
        {
            return Array.Empty<SassRuntimePack>();
        }

        var rid = GetRuntimeIdentifier(platform);
        var matches = new List<SassRuntimePack>();

        foreach (var pack in packs)
        {
            if (string.Equals(pack.Rid, rid, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(pack);
            }
        }

        matches.Sort(static (left, right) =>
        {
            var byPriority = right.Priority.CompareTo(left.Priority);
            if (byPriority != 0)
            {
                return byPriority;
            }

            var byId = string.CompareOrdinal(left.Id, right.Id);

            return byId != 0 ? byId : string.CompareOrdinal(left.RuntimesPath, right.RuntimesPath);
        });

        return matches;
    }

    /// <summary>
    /// Resolves the full path to the Sass executable.
    /// </summary>
    /// <param name="fileSystem">File system abstraction.</param>
    /// <param name="chmodProvider">Provider for setting executable permissions.</param>
    /// <param name="platform">Target platform.</param>
    /// <param name="runtimeDirectory">Optional explicit runtime directory. When set, it wins over <paramref name="runtimePacks"/>.</param>
    /// <param name="runtimePacks">Runtime packs contributed by the referenced runtime packages.</param>
    /// <param name="log">Optional sink for diagnostic messages about the selection.</param>
    /// <returns>Full path to the Sass executable.</returns>
    /// <exception cref="FileNotFoundException">No usable Sass executable could be found.</exception>
    public static string ResolveSassExecutable(
        IFileSystem fileSystem,
        IChmodProvider chmodProvider,
        Platform platform,
        string? runtimeDirectory = null,
        IReadOnlyList<SassRuntimePack>? runtimePacks = null,
        Action<string>? log = null)
    {
        // An explicit directory is a deliberate override, so it is never second-guessed against the packs.
        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            return ResolveFromDirectory(fileSystem, chmodProvider, platform, runtimeDirectory!);
        }

        var candidates = SelectPacks(runtimePacks, platform);
        var searched = new List<string>();

        foreach (var candidate in candidates)
        {
            var candidatePath = GetExecutablePath(candidate.RuntimesPath, platform);

            if (fileSystem.File.Exists(candidatePath))
            {
                if (candidates.Count > 1)
                {
                    log?.Invoke($"Selected Sass runtime pack {candidate} out of {candidates.Count} candidates for {GetRuntimeIdentifier(platform)}.");
                }
                else
                {
                    log?.Invoke($"Using Sass runtime pack {candidate}.");
                }

                chmodProvider.EnsureExecutablePermissions(candidatePath);

                return candidatePath;
            }

            searched.Add(candidatePath);
        }

        throw new FileNotFoundException(candidates.Count > 0
            ? BuildIncompletePackMessage(platform, candidates, searched)
            : BuildMissingPackMessage(platform, runtimePacks));
    }

    /// <summary>
    /// Resolves the Sass executable inside an explicitly configured runtimes directory.
    /// </summary>
    private static string ResolveFromDirectory(
        IFileSystem fileSystem,
        IChmodProvider chmodProvider,
        Platform platform,
        string runtimeDirectory)
    {
        var SassPath = GetExecutablePath(runtimeDirectory, platform);

        if (!fileSystem.File.Exists(SassPath))
        {
            var runtimePackageName = GetRuntimePackageName(platform);

            throw new FileNotFoundException(
                $"Sass executable not found at: {SassPath}\n\n" +
                $"SassRuntimeDirectory points at '{runtimeDirectory}', which does not contain a Sass build for {GetRuntimeIdentifier(platform)}.\n" +
                $"Either clear that property and reference the {runtimePackageName} package, or make sure the directory " +
                $"contains '{GetRuntimeIdentifier(platform)}/native/{GetInfo(platform).DirectoryName}/{GetExecutableName(platform)}'.");
        }

        chmodProvider.EnsureExecutablePermissions(SassPath);

        return SassPath;
    }

    /// <summary>
    /// Builds the error shown when no runtime pack targets the build host.
    /// </summary>
    private static string BuildMissingPackMessage(Platform platform, IEnumerable<SassRuntimePack>? allPacks)
    {
        var runtimePackageName = GetRuntimePackageName(platform);
        var rid = GetRuntimeIdentifier(platform);

        var message = new StringBuilder();
        message.Append("Sass runtime package not found.\n\n");
        message.Append($"No Sass runtime is available for this build host ({rid}).\n\n");
        message.Append("Add the matching runtime package to your project:\n");
        message.Append($"  <PackageReference Include=\"{runtimePackageName}\" Version=\"<Sass-version>\" PrivateAssets=\"all\" />\n\n");
        message.Append("...or let the build download Sass on demand:\n");
        message.Append("  <PropertyGroup>\n");
        message.Append("    <SassRuntimeDownload>true</SassRuntimeDownload>\n");
        message.Append("    <SassRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</SassRuntimeDirectory>\n");
        message.Append("  </PropertyGroup>\n\n");
        message.Append(DescribeVisiblePacks(allPacks));

        return message.ToString();
    }

    /// <summary>
    /// Builds the error shown when a matching runtime pack is referenced but its binary is missing.
    /// </summary>
    private static string BuildIncompletePackMessage(
        Platform platform,
        IReadOnlyList<SassRuntimePack> candidates,
        IReadOnlyList<string> searched)
    {
        var message = new StringBuilder();
        message.Append($"Sass executable not found at: {searched[0]}\n\n");
        message.Append(candidates.Count == 1
            ? $"The runtime pack {candidates[0]} is referenced but its Sass executable is missing.\n"
            : $"{candidates.Count} runtime packs target {GetRuntimeIdentifier(platform)} but none of them contains a Sass executable.\n");
        message.Append("Try clearing the NuGet cache for the runtime package and rebuilding.\n\n");
        message.Append("Locations searched:\n");

        foreach (var path in searched)
        {
            message.Append($"  - {path}\n");
        }

        return message.ToString();
    }

    /// <summary>
    /// Renders the packs the build can see, which is the fastest way to spot a host/pack mismatch.
    /// </summary>
    private static string DescribeVisiblePacks(IEnumerable<SassRuntimePack>? allPacks)
    {
        var message = new StringBuilder("Runtime packs visible to this project:");
        var any = false;

        if (allPacks is not null)
        {
            foreach (var pack in allPacks)
            {
                message.Append($"\n  - {pack}");
                any = true;
            }
        }

        if (!any)
        {
            message.Append(" (none)");
        }

        return message.ToString();
    }

    private static PlatformInfo GetInfo(Platform platform)
    {
        return PlatformMap.TryGetValue(platform, out var info)
            ? info
            : throw new ArgumentException($"Unknown platform: {platform}", nameof(platform));
    }
}
