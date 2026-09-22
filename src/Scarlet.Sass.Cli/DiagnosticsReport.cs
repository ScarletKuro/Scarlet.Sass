using System.Text;
using System.Text.Json;
using Scarlet.Sass.Core;

namespace Scarlet.Sass.Cli;

/// <summary>
/// Renders what the tool resolved and why.
/// </summary>
internal static class DiagnosticsReport
{
    /// <summary>
    /// Renders the human-readable report.
    /// </summary>
    /// <param name="resolution">The resolution to describe.</param>
    /// <param name="options">The configuration in effect.</param>
    /// <returns>The report, ending in a newline.</returns>
    public static string ToText(SassResolution resolution, SassCliOptions options)
    {
        var report = new StringBuilder();

        report.AppendLine();
        Append(report, "Scarlet.Sass.Cli", DescribePackage());
        Append(report, "Pinned Sass version", SassBuildInfo.PinnedSassVersion);
        Append(report, "Host platform", $"{resolution.Platform} ({resolution.RuntimeIdentifier})");
        Append(report, "Tool directory", AppContext.BaseDirectory);
        report.AppendLine();

        Append(report, "Sass executable", resolution.ExecutablePath ?? "(not present)");
        Append(report, "Source", DescribeSource(resolution));
        Append(report, "Requested version", resolution.RequestedVersion);
        Append(report, "Embedded probe path", resolution.EmbeddedProbePath);
        Append(report, "Cache root", resolution.CacheRoot);
        Append(report, "Runtime directory", resolution.RuntimeDirectory);
        Append(report, "Download URL", DescribeDownloadUrl(resolution));
        report.AppendLine();

        report.AppendLine("Environment");
        foreach (var (name, value) in DescribeEnvironment(options))
        {
            report.AppendLine($"  {name,-28}{value}");
        }

        return report.ToString();
    }

    /// <summary>
    /// Renders the same facts as a single JSON object, for scripting.
    /// </summary>
    /// <param name="resolution">The resolution to describe.</param>
    /// <param name="options">The configuration in effect.</param>
    /// <returns>Indented JSON, ending in a newline.</returns>
    public static string ToJson(SassResolution resolution, SassCliOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["package"] = DescribePackage(),
            ["pinnedSassVersion"] = SassBuildInfo.PinnedSassVersion,
            ["platform"] = resolution.Platform.ToString(),
            ["runtimeIdentifier"] = resolution.RuntimeIdentifier,
            ["toolDirectory"] = AppContext.BaseDirectory,
            ["SassExecutable"] = resolution.ExecutablePath,
            ["source"] = resolution.Source.ToString().ToLowerInvariant(),
            ["requestedVersion"] = resolution.RequestedVersion,
            ["embeddedProbePath"] = resolution.EmbeddedProbePath,
            ["cacheRoot"] = resolution.CacheRoot,
            ["runtimeDirectory"] = resolution.RuntimeDirectory,
            ["downloadUrl"] = resolution.IsResolved ? null : BuildDownloadUrl(resolution),
            ["failureReason"] = resolution.FailureReason,
            ["environment"] = new Dictionary<string, object?>
            {
                [SassCliOptions.PathVariable] = options.ExplicitSassPath,
                [SassCliOptions.VersionVariable] = options.RequestedVersionOverride,
                [SassCliOptions.CacheVariable] = options.CacheRootOverride,
                [SassCliOptions.NoEmbeddedVariable] = options.IgnoreEmbedded,
                [SassCliOptions.PassthroughVariable] = options.PurePassthrough,
                [SassCliOptions.DiagnosticsVariable] = options.Diagnostics,
                [SassCliOptions.DownloadTimeoutVariable] = options.DownloadTimeoutOverride
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static string DescribePackage() => DescribePackage(SassBuildInfo.PackagedRuntimeIdentifier);

    /// <summary>
    /// Names the package this build came from.
    /// </summary>
    /// <param name="packagedRuntimeIdentifier">The RID baked in at build time, empty for the portable package.</param>
    /// <returns>For example <c>Scarlet.Sass.Cli.win-x64</c>.</returns>
    /// <remarks>
    /// Takes the identifier rather than reading the constant so both branches are reachable from a test:
    /// the test build has no runtime identifier, so it could only ever exercise the portable one.
    /// </remarks>
    internal static string DescribePackage(string packagedRuntimeIdentifier)
    {
        return string.IsNullOrEmpty(packagedRuntimeIdentifier)
            ? "Scarlet.Sass.Cli (portable)"
            : $"Scarlet.Sass.Cli.{packagedRuntimeIdentifier}";
    }

    private static string DescribeSource(SassResolution resolution)
    {
        return resolution.Source switch
        {
            SassSource.Embedded => "embedded - shipped in this package, no network required",
            SassSource.Cache => "cache - downloaded by an earlier run",
            SassSource.Downloaded => "downloaded - fetched during this run",
            SassSource.Explicit => $"explicit - {SassCliOptions.PathVariable}",
            _ => "not found - would download on the next run"
        };
    }

    private static string DescribeDownloadUrl(SassResolution resolution)
    {
        return resolution.IsResolved ? "(not needed)" : BuildDownloadUrl(resolution);
    }

    private static string BuildDownloadUrl(SassResolution resolution)
    {
        var archiveExtension = SassRuntimeResolver.GetArchiveExtension(resolution.Platform);
        var archive = $"dart-sass-{resolution.RequestedVersion}-{SassRuntimeResolver.GetDownloadName(resolution.Platform)}.{archiveExtension}";

        return string.Equals(resolution.RequestedVersion, SassCliOptions.LatestVersion, StringComparison.OrdinalIgnoreCase)
            ? "https://github.com/sass/dart-sass/releases/latest"
            : $"https://github.com/sass/dart-sass/releases/download/{resolution.RequestedVersion}/{archive}";
    }

    private static IEnumerable<(string Name, string Value)> DescribeEnvironment(SassCliOptions options)
    {
        yield return (SassCliOptions.PathVariable, options.ExplicitSassPath ?? "(unset)");
        yield return (SassCliOptions.VersionVariable, options.RequestedVersionOverride ?? "(unset)");
        yield return (SassCliOptions.CacheVariable, options.CacheRootOverride ?? "(unset)");
        yield return (SassCliOptions.NoEmbeddedVariable, options.IgnoreEmbedded ? "enabled" : "(unset)");
        yield return (SassCliOptions.PassthroughVariable, options.PurePassthrough ? "enabled" : "(unset)");
        yield return (SassCliOptions.DiagnosticsVariable, options.Diagnostics ? "enabled" : "(unset)");
        yield return (SassCliOptions.DownloadTimeoutVariable, options.DownloadTimeoutOverride ?? "(unset)");
    }

    private static void Append(StringBuilder report, string label, string value) =>
        report.AppendLine($"{label,-22}{value}");
}
