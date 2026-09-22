using System;
using System.Collections.Generic;

namespace Scarlet.Sass.Core;

/// <summary>
/// Describes the process Scarlet should start for a Sass invocation.
/// </summary>
public sealed class SassLaunchCommand
{
    public SassLaunchCommand(string fileName, IReadOnlyList<string> arguments, string displayPath)
    {
        FileName = string.IsNullOrWhiteSpace(fileName)
            ? throw new ArgumentException("A launch file name is required.", nameof(fileName))
            : fileName;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        DisplayPath = string.IsNullOrWhiteSpace(displayPath)
            ? throw new ArgumentException("A display path is required.", nameof(displayPath))
            : displayPath;
    }

    /// <summary>
    /// The executable passed to <see cref="System.Diagnostics.ProcessStartInfo.FileName"/>.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Arguments that must be prepended before user or task Sass arguments.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// The user-facing Sass launcher path this command was resolved from.
    /// </summary>
    public string DisplayPath { get; }

    /// <summary>
    /// Creates a command that launches the executable directly with no fixed arguments.
    /// </summary>
    public static SassLaunchCommand FromExecutablePath(string executablePath) =>
        new(executablePath, Array.Empty<string>(), executablePath);
}
