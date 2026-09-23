using Microsoft.Build.Utilities;
using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

public class SassCompileTaskIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public SassCompileTaskIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompileTask_CompilesDirectoryAndExcludesPartials()
    {
        using var workspace = TempWorkspace.Create("compile-directory");
        workspace.WriteFile(
            "Sass/_variables.scss",
            "$brand: #663399;");
        workspace.WriteFile(
            "Sass/site.scss",
            """
            @use "variables";

            .banner {
              color: variables.$brand;
              &:hover {
                color: black;
              }
            }
            """);

        var item = new TaskItem("Sass");
        item.SetMetadata("OutputPath", @"wwwroot\css");
        item.SetMetadata("OutputStyle", "Expanded");
        item.SetMetadata("SourceMap", "true");

        var task = new SassCompileTask
        {
            BuildEngine = new MockBuildEngine(_output),
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Debug",
            OutputStyle = "Auto",
            SourceMap = "Auto",
            EmbedSources = "Auto",
            QuietDeps = "false",
            RuntimeDirectory = Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Sass.MSBuild", "bin", "runtimes")
        };

        Assert.True(task.Execute());

        var cssPath = workspace.PathTo("wwwroot", "css", "site.css");
        Assert.True(File.Exists(cssPath));
        Assert.True(File.Exists(cssPath + ".map"));
        Assert.False(File.Exists(workspace.PathTo("wwwroot", "css", "_variables.css")));
        Assert.Contains(".banner:hover", File.ReadAllText(cssPath));
        Assert.Contains(cssPath, task.GeneratedFiles.Select(static file => file.ItemSpec));
        Assert.Contains(
            task.GeneratedFiles,
            file => string.Equals(file.ItemSpec, cssPath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(file.GetMetadata("RelativePath"), Path.Combine("wwwroot", "css", "site.css"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompileTask_RemovesStaleOutputsWhenEntrypointDisappears()
    {
        using var workspace = TempWorkspace.Create("stale-output");
        workspace.WriteFile("Sass/one.scss", ".one { color: red; }");
        workspace.WriteFile("Sass/two.scss", ".two { color: blue; }");

        var item = new TaskItem("Sass");
        item.SetMetadata("OutputPath", "wwwroot/css");
        item.SetMetadata("SourceMap", "false");

        var task = CreateTask(workspace, item);
        Assert.True(task.Execute());
        Assert.True(File.Exists(workspace.PathTo("wwwroot", "css", "one.css")));
        Assert.True(File.Exists(workspace.PathTo("wwwroot", "css", "two.css")));

        File.Delete(workspace.PathTo("Sass", "two.scss"));

        var secondTask = CreateTask(workspace, item);
        Assert.True(secondTask.Execute());

        var removedPath = workspace.PathTo("wwwroot", "css", "two.css");
        Assert.False(File.Exists(removedPath));
        Assert.Contains(removedPath, secondTask.RemovedFiles.Select(static file => file.ItemSpec));
    }

    [Theory]
    [InlineData(RuntimeSelection.VersionDownload)]
    [InlineData(RuntimeSelection.RuntimePackItem)]
    public void CompileTask_WithChangedRuntimeSelection_ShouldRegenerate(RuntimeSelection changed)
    {
        using var workspace = TempWorkspace.Create("runtime-pack-stamp");
        workspace.WriteFile("Sass/site.scss", ".site { color: red; }");
        var runtimeDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");

        var item = new TaskItem("Sass/site.scss");
        item.SetMetadata("OutputPath", "wwwroot/css/site.css");
        item.SetMetadata("SourceMap", "false");

        var firstTask = new SassCompileTask
        {
            BuildEngine = new MockBuildEngine(_output),
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Release",
            OutputStyle = "Compressed",
            SourceMap = "false",
            EmbedSources = "false",
            QuietDeps = "false",
            RuntimeDirectory = runtimeDirectory
        };
        Assert.True(firstTask.Execute());

        var cssPath = workspace.PathTo("wwwroot", "css", "site.css");
        var directoryName = Path.GetDirectoryName(cssPath);
        Assert.NotNull(directoryName);
        Directory.CreateDirectory(directoryName);
        File.WriteAllText(cssPath, "stale output");

        // RuntimeDirectory stays set and wins over runtime pack selection, so the second run still resolves
        // the same fake Sass and succeeds. The assertion is only about whether the stamp noticed the change.
        var secondTask = new SassCompileTask
        {
            BuildEngine = new MockBuildEngine(_output),
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Release",
            OutputStyle = "Compressed",
            SourceMap = "false",
            EmbedSources = "false",
            QuietDeps = "false",
            RuntimeDirectory = runtimeDirectory
        };

        switch (changed)
        {
            case RuntimeSelection.VersionDownload:
                secondTask.SassVersionDownload = "1.2.3";
                break;

            case RuntimeSelection.RuntimePackItem:
                var pack = new TaskItem("Contoso.Sass.Runtime.custom");
                pack.SetMetadata(SassRuntimePack.RidMetadataName, SassRuntimeResolver.GetRuntimeIdentifier(SassRuntimeResolver.GetCurrentPlatform()));
                pack.SetMetadata(SassRuntimePack.RuntimesPathMetadataName, workspace.PathTo("custom-runtimes"));
                pack.SetMetadata(SassRuntimePack.PriorityMetadataName, "50");
                secondTask.RuntimePacks = [pack];
                break;
        }

        Assert.True(secondTask.Execute());

        Assert.True(File.Exists(cssPath));
        Assert.NotEqual("stale output", File.ReadAllText(cssPath));
    }

    [Fact]
    public void CompileTask_WhenTimeoutElapses_KillsTheProcessAndFails()
    {
        using var workspace = TempWorkspace.Create("timeout");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");

        var item = new TaskItem("Sass");
        item.SetMetadata("OutputPath", "wwwroot/css");
        item.SetMetadata("AdditionalArguments", "--watch --poll");

        var buildEngine = new MockBuildEngine(_output);
        var task = new SassCompileTask
        {
            BuildEngine = buildEngine,
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Debug",
            OutputStyle = "Auto",
            SourceMap = "Auto",
            EmbedSources = "Auto",
            QuietDeps = "false",
            RuntimeDirectory = Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Sass.MSBuild", "bin", "runtimes"),
            // Watch mode intentionally keeps Sass alive after the initial compile, so the timeout assertion
            // does not depend on host speed or Dart VM startup cost.
            TimeoutMilliseconds = 200
        };

        Assert.False(task.Execute());
        Assert.Contains(buildEngine.Errors, e => e.Message != null && e.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    private static SassCompileTask CreateTask(TempWorkspace workspace, TaskItem item)
    {
        return new SassCompileTask
        {
            BuildEngine = new MockBuildEngine(),
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Release",
            OutputStyle = "Compressed",
            SourceMap = "false",
            EmbedSources = "false",
            QuietDeps = "false",
            RuntimeDirectory = Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Sass.MSBuild", "bin", "runtimes")
        };
    }

    public enum RuntimeSelection
    {
        VersionDownload,
        RuntimePackItem
    }
}
