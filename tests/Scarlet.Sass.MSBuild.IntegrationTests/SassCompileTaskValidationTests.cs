using Microsoft.Build.Utilities;
using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

/// <summary>
/// Covers the task's input validation and its translation of settings into a Dart Sass command line.
/// </summary>
/// <remarks>
/// These run the task in-process rather than through MSBuild on purpose. The equivalent .targets-driven
/// tests build in a separate process, so nothing they exercise shows up as covered here - which is how a
/// whole column of argument builders and every validation message ended up with no coverage at all despite
/// the behaviour working.
///
/// The validation cases deliberately set no <c>RuntimeDirectory</c>: each one is rejected before the task
/// resolves a runtime, so needing one would only be a way for the test to fail for the wrong reason.
/// </remarks>
public class SassCompileTaskValidationTests
{
    private readonly ITestOutputHelper _output;

    public SassCompileTaskValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompileTask_WithUnknownOutputStyle_ShouldFailWithAMessageNamingTheValue()
    {
        using var workspace = TempWorkspace.Create("validate-output-style");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");

        var (task, engine) = CreateTask(workspace, Item("Sass", "wwwroot/css"));
        task.OutputStyle = "Minified";

        Assert.False(task.Execute());
        Assert.Contains(engine.Errors, e => e.Message?.Contains("SassOutputStyle must be Auto, Expanded, or Compressed", StringComparison.Ordinal) == true);
        // The offending value has to appear, or the user has to guess which of their settings was rejected.
        Assert.Contains(engine.Errors, e => e.Message?.Contains("Minified", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("QuietDeps")]
    [InlineData("SourceMap")]
    [InlineData("EmbedSources")]
    public void CompileTask_WithNonBooleanSetting_ShouldFailWithAMessageNamingTheSetting(string setting)
    {
        using var workspace = TempWorkspace.Create($"validate-bool-{setting}");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");

        var (task, engine) = CreateTask(workspace, Item("Sass", "wwwroot/css"));
        switch (setting)
        {
            case "QuietDeps":
                task.QuietDeps = "perhaps";
                break;
            case "SourceMap":
                task.SourceMap = "perhaps";
                break;
            default:
                task.EmbedSources = "perhaps";
                break;
        }

        Assert.False(task.Execute());
        Assert.Contains(engine.Errors, e => e.Message?.Contains("must be true or false", StringComparison.Ordinal) == true);
        Assert.Contains(engine.Errors, e => e.Message?.Contains("perhaps", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_WithItemMissingOutputPath_ShouldFail()
    {
        using var workspace = TempWorkspace.Create("validate-missing-output");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");

        var (task, engine) = CreateTask(workspace, new TaskItem("Sass"));

        Assert.False(task.Execute());
        Assert.Contains(engine.Errors, e => e.Message?.Contains("must specify OutputPath metadata", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_WithDirectoryInputAndFileOutputPath_ShouldFail()
    {
        // A directory of stylesheets compiles to a directory of CSS. Pointing that at an existing file would
        // otherwise be handed to Dart Sass to fail on, with a message about a path the user never wrote.
        using var workspace = TempWorkspace.Create("validate-dir-to-file");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");
        workspace.WriteFile("wwwroot/css/site.css", "/* already a file */");

        var (task, engine) = CreateTask(workspace, Item("Sass", "wwwroot/css/site.css"));

        Assert.False(task.Execute());
        Assert.Contains(engine.Errors, e => e.Message?.Contains("must be a directory", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_WithMissingInput_ShouldFail()
    {
        using var workspace = TempWorkspace.Create("validate-missing-input");

        var (task, engine) = CreateTask(workspace, Item("Sass/does-not-exist.scss", "wwwroot/css"));

        Assert.False(task.Execute());
        Assert.Contains(engine.Errors, e => e.Message?.Contains("was not found", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_WithNoCompilations_ShouldSucceedWithoutResolvingARuntime()
    {
        // The target's condition keeps this from happening in a real build, but the guard has to hold on its
        // own: returning false here would fail builds that simply have no stylesheets yet, and resolving a
        // runtime first would make an empty build pay for a download.
        using var workspace = TempWorkspace.Create("validate-no-entries");

        var engine = new MockBuildEngine(_output);
        var task = new SassCompileTask
        {
            BuildEngine = engine,
            Compilations = [],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Debug",
            OutputStyle = "Auto",
            SourceMap = "Auto",
            EmbedSources = "Auto",
            QuietDeps = "false"
        };

        Assert.True(task.Execute());
        Assert.Empty(engine.Errors);
        Assert.Contains(engine.Messages, m => m.Message?.Contains("No Sass entry points were found", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_ShouldTranslateEverySettingIntoACommandLineFlag()
    {
        // Asserted against the "Executing:" line the task logs, which is the command actually handed to Dart
        // Sass. Each of these flags had no coverage at all: the settings were reachable only through the
        // out-of-process .targets tests, so deleting any one of these builders broke nothing here.
        using var workspace = TempWorkspace.Create("arguments");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");
        workspace.WriteFile("Shared/_tokens.scss", "$accent: #663399;");

        var (task, engine) = CreateTask(workspace, Item("Sass", "wwwroot/css"), withRuntime: true);
        task.QuietDeps = "true";
        task.LoadPaths = "Shared";
        task.PkgImporter = "node";
        task.SilenceDeprecations = "import;global-builtin";
        task.FatalDeprecations = "color-functions";

        task.Execute();

        var executing = Assert.Single(engine.Messages, m => m.Message?.StartsWith("Executing:", StringComparison.Ordinal) == true).Message!;
        _output.WriteLine(executing);

        Assert.Contains("--quiet-deps", executing, StringComparison.Ordinal);
        Assert.Contains("--load-path=", executing, StringComparison.Ordinal);
        Assert.Contains("Shared", executing, StringComparison.Ordinal);
        Assert.Contains("--pkg-importer=node", executing, StringComparison.Ordinal);
        // Semicolon-separated lists become one flag per value rather than one flag with a joined value.
        Assert.Contains("--silence-deprecation=import", executing, StringComparison.Ordinal);
        Assert.Contains("--silence-deprecation=global-builtin", executing, StringComparison.Ordinal);
        Assert.Contains("--fatal-deprecation=color-functions", executing, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileTask_WithStampDirectorySet_ShouldWriteTheStampAndManifestThere()
    {
        // The .targets test for this builds out of process, so the override branch itself was never executed
        // under coverage; only the default "obj/Scarlet.Sass" path was.
        using var workspace = TempWorkspace.Create("stamp-directory");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");

        var (task, _) = CreateTask(workspace, Item("Sass", "wwwroot/css"), withRuntime: true);
        task.StampDirectory = "custom-stamps";

        Assert.True(task.Execute());

        var stampDirectory = Path.Combine(workspace.RootDirectory, "custom-stamps");
        Assert.True(File.Exists(Path.Combine(stampDirectory, "Sass.settings.stamp")), "Settings stamp was not written to StampDirectory.");
        Assert.True(File.Exists(Path.Combine(stampDirectory, "Sass.generated.txt")), "Generated-files manifest was not written to StampDirectory.");
        Assert.False(Directory.Exists(Path.Combine(workspace.RootDirectory, "obj", "Scarlet.Sass")), "The default stamp directory was used despite StampDirectory being set.");
    }

    [Fact]
    public void CompileTask_WithRuntimePackMissingRid_ShouldWarnAndKeepBuilding()
    {
        // The pack is skipped rather than fatal, so without a warning a typo in a hand-authored
        // SassRuntimePack is completely silent - the build works, from a pack the author did not intend.
        using var workspace = TempWorkspace.Create("pack-missing-rid");
        workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");

        var (task, engine) = CreateTask(workspace, Item("Sass", "wwwroot/css"), withRuntime: true);
        task.RuntimePacks = [new TaskItem("Zephyr.Sass.Typo")];

        Assert.True(task.Execute());
        Assert.Contains(engine.Warnings, w => w.Message?.Contains("Zephyr.Sass.Typo", StringComparison.Ordinal) == true);
        Assert.Contains(engine.Warnings, w => w.Message?.Contains("Rid", StringComparison.Ordinal) == true);
    }

    private static TaskItem Item(string include, string outputPath)
    {
        var item = new TaskItem(include);
        item.SetMetadata("OutputPath", outputPath);
        return item;
    }

    private (SassCompileTask Task, MockBuildEngine Engine) CreateTask(
        TempWorkspace workspace,
        TaskItem item,
        bool withRuntime = false)
    {
        var engine = new MockBuildEngine(_output);
        var task = new SassCompileTask
        {
            BuildEngine = engine,
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Debug",
            OutputStyle = "Auto",
            SourceMap = "Auto",
            EmbedSources = "Auto",
            QuietDeps = "false",
            RuntimeDirectory = withRuntime
                ? Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Sass.MSBuild", "bin", "runtimes")
                : null
        };

        return (task, engine);
    }
}
