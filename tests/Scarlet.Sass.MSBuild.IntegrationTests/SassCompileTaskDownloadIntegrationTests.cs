using Microsoft.Build.Utilities;
using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

/// <summary>
/// Covers <see cref="SassCompileTask"/>'s <c>SassRuntimeDownload</c> mode end to end, against the real
/// GitHub release feed. This drives the download through the compile task itself, because Scarlet.Sass
/// deliberately does not expose a generic command runner.
/// </summary>
public class SassCompileTaskDownloadIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public SassCompileTaskDownloadIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompileTask_WithRuntimeDownload_ShouldDownloadLatestAndCompile()
    {
        using var workspace = TempWorkspace.Create("download-latest");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");
        var tempRuntimeDirectory = Path.Combine(Path.GetTempPath(), $"scarlet-sass-download-test-{Guid.NewGuid():N}");

        var item = new TaskItem("Sass");
        item.SetMetadata("OutputPath", "wwwroot/css");

        try
        {
            var task = new SassCompileTask
            {
                BuildEngine = new MockBuildEngine(_output),
                Compilations = new[] { item },
                ProjectDirectory = workspace.RootDirectory,
                Configuration = "Debug",
                OutputStyle = "Auto",
                SourceMap = "Auto",
                EmbedSources = "Auto",
                QuietDeps = "false",
                RuntimeDirectory = tempRuntimeDirectory,
                SassRuntimeDownload = true
                // SassVersionDownload intentionally omitted - exercises the "resolve latest" path.
            };

            Assert.True(task.Execute());

            var cssPath = workspace.PathTo("wwwroot", "css", "site.css");
            Assert.True(File.Exists(cssPath));
            Assert.Contains(".banner", File.ReadAllText(cssPath));

            var platform = SassRuntimeResolver.GetCurrentPlatform();
            var launcherPath = SassRuntimeResolver.GetLauncherPath(tempRuntimeDirectory, platform);
            Assert.True(File.Exists(launcherPath), $"Expected the downloaded runtime at {launcherPath}");
            _output.WriteLine($"Runtime downloaded to: {launcherPath}");
        }
        finally
        {
            if (Directory.Exists(tempRuntimeDirectory))
            {
                try
                {
                    Directory.Delete(tempRuntimeDirectory, recursive: true);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Cleanup failed: {ex.Message}");
                }
            }
        }
    }

    [Fact]
    public void CompileTask_WithRuntimeDownloadAndNoRuntimeDirectory_ShouldFail()
    {
        using var workspace = TempWorkspace.Create("download-no-directory");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");

        var item = new TaskItem("Sass");
        item.SetMetadata("OutputPath", "wwwroot/css");

        var buildEngine = new MockBuildEngine(_output);
        var task = new SassCompileTask
        {
            BuildEngine = buildEngine,
            Compilations = new[] { item },
            ProjectDirectory = workspace.RootDirectory,
            SassRuntimeDownload = true
            // RuntimeDirectory not specified - should fail without ever touching the network.
        };

        Assert.False(task.Execute());
        Assert.Contains(buildEngine.Errors, e => e.Message != null && e.Message.Contains("SassRuntimeDirectory", StringComparison.Ordinal));
    }
}
