using System.Diagnostics.CodeAnalysis;
using Scarlet.Sass.Core;

namespace Scarlet.Sass.Cli;

/// <summary>
/// The outcome of resolving a Sass launcher, including the context needed to explain it.
/// </summary>
/// <param name="LaunchCommand">The command to start, or <see langword="null"/> when no Sass runtime was resolved.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Platform">The detected host platform.</param>
/// <param name="RuntimeIdentifier">The runtime identifier for <paramref name="Platform"/>.</param>
/// <param name="RequestedVersion">The version that was asked for.</param>
/// <param name="CacheRoot">The per-user cache root in effect.</param>
/// <param name="RuntimeDirectory">The version-scoped directory downloads go to.</param>
/// <param name="EmbeddedProbePath">Where an embedded Sass launcher would have been, for diagnostics.</param>
/// <param name="FailureReason">Why resolution failed, when it did.</param>
internal sealed record SassResolution(
    SassLaunchCommand? LaunchCommand,
    SassSource Source,
    Platform Platform,
    string RuntimeIdentifier,
    string RequestedVersion,
    string CacheRoot,
    string RuntimeDirectory,
    string EmbeddedProbePath,
    string? FailureReason)
{
    /// <summary>The resolved public Sass launcher, or <see langword="null"/> when no Sass runtime was found.</summary>
    public string? LauncherPath => LaunchCommand?.DisplayPath;

    /// <summary>Whether a usable Sass launch command was resolved.</summary>
    [MemberNotNullWhen(true, nameof(LaunchCommand))]
    public bool IsResolved => LaunchCommand is not null;
}
