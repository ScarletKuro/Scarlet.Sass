using System.Diagnostics;
using System.Xml.Linq;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

/// <summary>
/// Asserts that the Sass binary staged for this platform really is the version the repository claims.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a bug that shipped silently: <c>tools/download-sass.ps1</c> and
/// <c>download-sass.sh</c> used to extract the archive into the project directory and then search that same
/// directory for the executable. The search found the <em>existing</em> binary before the freshly extracted
/// one, concluded it was already in place, skipped the move - and still wrote the version marker. Every
/// version bump after the first download therefore kept the old binary and relabelled it, so
/// <c>Scarlet.Sass.Runtime.* 1.4.2</c> would have shipped Sass 1.3.14.
/// </para>
/// <para>
/// Nothing else catches that: the marker file agrees with itself, and a binary for another platform cannot
/// be executed to check. Only the host platform's binary can be asked, which is why this runs per CI leg.
/// </para>
/// </remarks>
public class SassBinaryVersionTests
{
    [Fact]
    public void StagedSassBinary_ShouldReportTheVersionTheRepositoryPinned()
    {
        // Arrange
        var expectedVersion = ReadPinnedSassVersion();
        var platform = SassRuntimeResolver.GetCurrentPlatform();
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var SassPath = SassRuntimeResolver.GetLauncherPath(runtimesDirectory, platform);

        Assert.True(
            File.Exists(SassPath),
            $"No Sass binary staged for {platform} at '{SassPath}'. Build Scarlet.Sass.MSBuild first.");

        // Act
        var reportedVersion = RunSass(SassPath, "--version");

        // Assert
        Assert.Equal(expectedVersion, reportedVersion);
    }

    private static string ReadPinnedSassVersion()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Directory.Build.props");

            if (File.Exists(candidate))
            {
                var root = XDocument.Load(candidate).Root;
                Assert.NotNull(root);

                return Assert.Single(root.Descendants("SassVersion")).Value.Trim();
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate Directory.Build.props above '{AppContext.BaseDirectory}'.");
    }

    private static string RunSass(string SassPath, string argument)
    {
        var startInfo = new ProcessStartInfo(SassPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"'{SassPath} {argument}' exited with {process.ExitCode}.");

        return output.Trim();
    }
}
