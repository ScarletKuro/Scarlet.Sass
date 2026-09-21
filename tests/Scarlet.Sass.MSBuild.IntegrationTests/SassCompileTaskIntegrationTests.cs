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
        item.SetMetadata("OutputPath", "wwwroot/css");
        item.SetMetadata("OutputStyle", "Expanded");
        item.SetMetadata("SourceMap", "true");

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

    private static SassCompileTask CreateTask(TempWorkspace workspace, TaskItem item)
    {
        return new SassCompileTask
        {
            BuildEngine = new MockBuildEngine(),
            Compilations = new[] { item },
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Release",
            OutputStyle = "Compressed",
            SourceMap = "false",
            EmbedSources = "false",
            QuietDeps = "false",
            RuntimeDirectory = Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Sass.MSBuild", "bin", "runtimes")
        };
    }
}
