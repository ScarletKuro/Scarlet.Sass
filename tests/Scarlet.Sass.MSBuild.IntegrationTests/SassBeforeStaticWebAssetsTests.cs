using System.IO.Compression;
using System.Security;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

/// <summary>
/// Covers the <c>SassBeforeStaticWebAssets</c> item contract end to end, by running a real build and
/// asserting on what it produces. The point of the target is that files Sass writes into <c>wwwroot</c>
/// during the build - and so are absent when MSBuild evaluates the project - still reach the static web
/// assets pipeline, which is only observable from the package or publish output.
/// </summary>
public class SassBeforeStaticWebAssetsTests
{
    private const string GeneratedAsset = "css/generated.css";

    /// <summary>The TFM the generated test projects target; also the intermediate output path segment.</summary>
    private const string TargetFramework = "net10.0";

    private readonly ITestOutputHelper _output;

    public SassBeforeStaticWebAssetsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SingleTargetFrameworkPack_IncludesGeneratedWwwrootFilesAsStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
            </SassBeforeStaticWebAssets>
            """);

        await Pack(workspace);

        // staticwebassets/ is what makes the file reachable as _content/<PackageId>/css/generated.css in a
        // consuming app. An absolute Content glob still packs the file, but under content/ and
        // contentFiles/, where no consumer will ever serve it.
        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task RepeatedPack_DoesNotDuplicateGeneratedStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
            </SassBeforeStaticWebAssets>
            """);

        await Pack(workspace);

        // The second pack evaluates the project with wwwroot already populated, so the SDK's own glob has
        // already claimed the generated file and the target re-adds it on top. Nothing about that path is
        // exercised by a clean build, which is the only case the other tests cover.
        await Pack(workspace);

        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task WebProjectPublish_FingerprintsGeneratedStaticWebAssets()
    {
        using var workspace = CreateWebApplication();

        var result = await RunDotnet(workspace, $"publish --configuration {DotnetCli.Configuration} --output publish");
        Assert.Equal(0, result.ExitCode);

        var endpoints = Assert.Single(
            Directory.GetFiles(workspace.PathTo("publish"), "*.staticwebassets.endpoints.json"));
        var manifest = File.ReadAllText(endpoints);

        // A fingerprinted route proves the file went through the static web assets pipeline rather than
        // being copied to the publish directory as plain content, which is what the README promises.
        Assert.Matches(new Regex("""
            "Route"\s*:\s*"css/generated\.[a-z0-9]+\.css"
            """.Trim()), manifest);
    }

    [Fact]
    public async Task Steps_RunInDeclarationOrder()
    {
        // The whole feature rests on this: `Sass install` has to finish before `Sass run build.mjs` starts.
        // Ordering falls out of MSBuild's task batching following item declaration order, which is an
        // implementation detail worth pinning rather than assuming.
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>first.mjs</Arguments>
            </SassBeforeStaticWebAssets>
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>second.mjs</Arguments>
            </SassBeforeStaticWebAssets>
            """);

        workspace.WriteFile("first.mjs", AppendToOrderLog("first"));
        workspace.WriteFile("second.mjs", AppendToOrderLog("second"));

        await Pack(workspace);

        // `dotnet pack` runs the steps in both its build and its pack pass, so assert the shape of the log
        // rather than a single pair: what matters is that "second" never precedes "first".
        Assert.Matches("^(first,second,)+$", File.ReadAllText(workspace.PathTo("order.log")));
    }

    [Fact]
    public async Task WorkingDirectoryMetadata_RunsTheStepInThatDirectory()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>generate.mjs</Arguments>
              <WorkingDirectory>$(MSBuildProjectDirectory)/tools</WorkingDirectory>
            </SassBeforeStaticWebAssets>
            """);

        File.Delete(workspace.PathTo("build.mjs"));

        // Both the script path and the path it writes to are resolved from the working directory, so the
        // step only produces the asset in the right place if WorkingDirectory was honoured.
        workspace.WriteFile("tools/generate.mjs", WriteGeneratedAsset("../wwwroot/css"));

        await Pack(workspace);

        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task ContinueOnErrorMetadata_KeepsTheBuildGreenWhenAStepFails()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
            </SassBeforeStaticWebAssets>
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>fail.mjs</Arguments>
              <ContinueOnError>true</ContinueOnError>
            </SassBeforeStaticWebAssets>
            """);

        workspace.WriteFile("fail.mjs", "process.exit(3);");

        await Pack(workspace);

        // The failing step is downgraded to a warning, and the step before it still contributed its asset.
        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task FailingStep_FailsTheBuildByDefault()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>fail.mjs</Arguments>
            </SassBeforeStaticWebAssets>
            """);

        workspace.WriteFile("fail.mjs", "process.exit(3);");

        var result = await RunDotnet(workspace, $"pack --configuration {DotnetCli.Configuration} --output nupkg");

        Assert.NotEqual(0, result.ExitCode);
    }

    private static string AppendToOrderLog(string step) =>
        Script($"""fs.appendFileSync("order.log", "{step},");""", WriteGeneratedAssetTo("wwwroot/css"));

    private static string WriteGeneratedAsset(string directory) =>
        Script(WriteGeneratedAssetTo(directory));

    private static string WriteGeneratedAssetTo(string directory) =>
        // The existsSync guard is not defensive padding: Sass's recursive mkdirSync throws EEXIST for a
        // relative path that walks up through "..", and the steps run more than once per `dotnet pack`.
        $$"""
        if (!fs.existsSync("{{directory}}")) {
            fs.mkdirSync("{{directory}}", { recursive: true });
        }

        fs.writeFileSync("{{directory}}/generated.css", "body{color:red}");
        """;

    /// <summary>
    /// Wraps script bodies in a single <c>node:fs</c> import; importing it twice is a syntax error.
    /// </summary>
    private static string Script(params string[] bodies) =>
        $"""
        import fs from "node:fs";

        {string.Join(Environment.NewLine, bodies)}
        """;

    /// <summary>
    /// Drives incremental skipping through MSBuild rather than by constructing the task directly, because the
    /// item metadata has to reach the task's parameters for any of it to happen.
    /// </summary>
    /// <remarks>
    /// Deleting the Inputs and Outputs attributes from both targets files leaves every task-level incremental
    /// test passing: the feature degrades to "always runs", which no assertion anywhere notices. This is the
    /// test that fails when that plumbing breaks.
    /// </remarks>
    [Fact]
    public async Task IncrementalMetadata_SkipsUnchangedStepsAndRerunsAfterASourceChange()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
              <Inputs>assets/app.js</Inputs>
              <Outputs>wwwroot/css/generated.css</Outputs>
            </SassBeforeStaticWebAssets>
            """);

        workspace.WriteFile("assets/app.js", "// v1");

        Assert.Equal(1, await BuildAndCountSassRuns(workspace));
        Assert.Equal(0, await BuildAndCountSassRuns(workspace));

        workspace.WriteFile("assets/app.js", "// v2");
        Assert.Equal(1, await BuildAndCountSassRuns(workspace));

        // The stamp belongs to the project's real intermediate output, and the exact directory is the
        // assertion: the task's own fallback is obj\Scarlet.Sass, which is still "somewhere under obj with
        // Scarlet.Sass in the path" - so anything looser passes when StampDirectory is not wired through at
        // all, and only the clean below would notice, under a message blaming the wrong thing.
        var stamp = Assert.Single(Directory.GetFiles(
            workspace.PathTo("obj"), "*.stamp", SearchOption.AllDirectories));

        Assert.Equal(
            workspace.PathTo("obj", DotnetCli.Configuration, TargetFramework, "Scarlet.Sass"),
            Path.GetDirectoryName(stamp));

        var clean = await RunDotnet(workspace, $"clean --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, clean.ExitCode);
        Assert.False(File.Exists(stamp), "dotnet clean should remove the incremental stamp.");
    }

    /// <summary>
    /// The last of the documented metadata to reach the task. Unwired, the timeout falls back to
    /// <c>$(SassTimeoutMilliseconds)</c>, which defaults to 0 - no limit - so a step that should have been
    /// killed simply runs to completion and the build goes green.
    /// </summary>
    [Fact]
    public async Task TimeoutMillisecondsMetadata_ShouldKillAStepThatOverruns()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>slow.mjs</Arguments>
              <TimeoutMilliseconds>2000</TimeoutMilliseconds>
            </SassBeforeStaticWebAssets>
            """);

        // Far longer than the timeout, so the outcome cannot turn on scheduling noise.
        workspace.WriteFile("slow.mjs", "await Sass.sleep(120000);");

        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("timed out after 2000ms", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Relative <c>Inputs</c> and <c>Outputs</c> resolve against the project, not the working directory.
    /// </summary>
    /// <remarks>
    /// Needs both halves to mean anything: a step whose <c>WorkingDirectory</c> is somewhere other than the
    /// project, *and* relative paths. With them equal - which is the default, and what every other test here
    /// uses - the task's fallback produces the same answer as the wiring, so dropping
    /// <c>ProjectDirectory</c> from the task call changes nothing observable. Here it changes everything:
    /// the inputs resolve under tools\, are not found, and the step silently stops skipping.
    /// </remarks>
    [Fact]
    public async Task RelativeIncrementalPaths_ShouldResolveAgainstTheProjectNotTheWorkingDirectory()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>generate.mjs</Arguments>
              <WorkingDirectory>$(MSBuildProjectDirectory)/tools</WorkingDirectory>
              <Inputs>assets/app.js</Inputs>
              <Outputs>wwwroot/css/generated.css</Outputs>
            </SassBeforeStaticWebAssets>
            """);

        File.Delete(workspace.PathTo("build.mjs"));
        workspace.WriteFile("tools/generate.mjs", WriteGeneratedAsset("../wwwroot/css"));
        workspace.WriteFile("assets/app.js", "// v1");

        Assert.Equal(1, await BuildAndCountSassRuns(workspace));

        // Skipping at all proves the inputs were found, which only happens from the project directory.
        Assert.Equal(0, await BuildAndCountSassRuns(workspace));

        workspace.WriteFile("assets/app.js", "// v2");
        Assert.Equal(1, await BuildAndCountSassRuns(workspace));
    }

    /// <summary>
    /// <c>StampFile</c> is the third half of the incremental contract, and the only one whose absence is
    /// invisible: drop its attribute from the task call and the step still skips, just from the generated
    /// path instead of the requested one. Nothing else notices, so this asserts the location itself.
    /// </summary>
    [Fact]
    public async Task StampFileMetadata_ShouldPutTheStampWhereItAsks()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
              <Inputs>assets/app.js</Inputs>
              <Outputs>wwwroot/css/generated.css</Outputs>
              <StampFile>custom/Sass-assets.stamp</StampFile>
            </SassBeforeStaticWebAssets>
            """);

        workspace.WriteFile("assets/app.js", "// v1");

        Assert.Equal(1, await BuildAndCountSassRuns(workspace));

        // Relative to the project, like every other path in a project file.
        var requested = workspace.PathTo("custom", "Sass-assets.stamp");
        Assert.True(File.Exists(requested), $"Expected the stamp at {requested}.");

        // And nowhere else: a generated stamp under obj would mean StampFile was ignored and the step is
        // skipping on a path the project never asked for.
        Assert.Empty(Directory.Exists(workspace.PathTo("obj"))
            ? Directory.GetFiles(workspace.PathTo("obj"), "*.stamp", SearchOption.AllDirectories)
            : []);

        // Still a working stamp, not just a file in the right place.
        Assert.Equal(0, await BuildAndCountSassRuns(workspace));

        workspace.WriteFile("assets/app.js", "// v2");
        Assert.Equal(1, await BuildAndCountSassRuns(workspace));
    }

    /// <summary>
    /// A skipped step still has to leave a correct package behind: the generated files were produced by an
    /// earlier build, and it is the target's Content re-add - not the Sass run - that carries them into the
    /// static web assets pipeline.
    /// </summary>
    [Fact]
    public async Task IncrementalSkip_StillPacksTheGeneratedStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <SassBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
              <Inputs>assets/app.js</Inputs>
              <Outputs>wwwroot/css/generated.css</Outputs>
            </SassBeforeStaticWebAssets>
            """);

        workspace.WriteFile("assets/app.js", "// v1");

        Assert.Equal(1, await BuildAndCountSassRuns(workspace));
        Assert.Equal(0, await BuildAndCountSassRuns(workspace));

        await Pack(workspace);

        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    private async Task<int> BuildAndCountSassRuns(TempWorkspace workspace)
    {
        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration} --verbosity normal");
        Assert.Equal(0, result.ExitCode);

        return result.Output.Split('\n').Count(line => line.Contains("Executing: Sass ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Razor Class Library is the strictest case: its wwwroot files have to be packed under
    /// staticwebassets/ for a consuming app to serve them.
    /// </summary>
    private TempWorkspace CreateRazorClassLibrary(string steps)
    {
        var workspace = TempWorkspace.Create("static-web-assets");

        try
        {
            workspace.WriteFile("build.mjs", WriteGeneratedAsset("wwwroot/css"));
            workspace.WriteFile("GeneratedAssetsRcl.csproj", Project("Microsoft.NET.Sdk.Razor", steps));

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private TempWorkspace CreateWebApplication()
    {
        var workspace = TempWorkspace.Create("static-web-assets-web");

        try
        {
            workspace.WriteFile("build.mjs", WriteGeneratedAsset("wwwroot/css"));
            workspace.WriteFile(
                "Program.cs",
                "WebApplication.CreateBuilder(args).Build().Run();");
            workspace.WriteFile(
                "GeneratedAssetsWeb.csproj",
                Project(
                    "Microsoft.NET.Sdk.Web",
                    """
                    <SassBeforeStaticWebAssets Include="run">
                      <Arguments>build.mjs</Arguments>
                    </SassBeforeStaticWebAssets>
                    """,
                    additionalProperties: "<ImplicitUsings>enable</ImplicitUsings>"));

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static string Project(string sdk, string steps, string additionalProperties = "") =>
        $"""
        <Project Sdk="{sdk}">
          <PropertyGroup>
            <TargetFramework>{TargetFramework}</TargetFramework>
            <IsPackable>true</IsPackable>
            {additionalProperties}
          </PropertyGroup>

          <ItemGroup>
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
          </ItemGroup>

          <Import Project="{SecurityElement.Escape(DevelopmentTargetsPath)}" />

          <ItemGroup>
        {steps}
          </ItemGroup>
        </Project>
        """;

    private static string DevelopmentTargetsPath => Path.Combine(
        RepositoryRoot.Path, "src", "Scarlet.Sass.MSBuild", "Scarlet.Sass.MSBuild.targets");

    private async Task Pack(TempWorkspace workspace)
    {
        var result = await RunDotnet(workspace, $"pack --configuration {DotnetCli.Configuration} --output nupkg");

        Assert.Equal(0, result.ExitCode);
    }

    private async Task<DotnetResult> RunDotnet(TempWorkspace workspace, string arguments)
    {
        var result = await DotnetCli.Run(workspace.RootDirectory, arguments);
        _output.WriteLine(result.Output);

        return result;
    }

    private static int CountPackageEntries(TempWorkspace workspace, string entryName)
    {
        var packagePath = Assert.Single(Directory.GetFiles(workspace.PathTo("nupkg"), "*.nupkg"));

        using var package = ZipFile.OpenRead(packagePath);

        return package.Entries.Count(entry => entry.FullName == entryName);
    }
}
