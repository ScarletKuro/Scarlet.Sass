using System.IO.Compression;
using System.Security;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

/// <summary>
/// Covers the <c>RunSassBeforeStaticWebAssets</c> target end to end, by running real <c>dotnet build</c>,
/// <c>pack</c> and <c>publish</c> invocations and asserting on what they produce - the counterpart to
/// <c>BunBeforeStaticWebAssetsTests</c> in Scarlet.Bun.
/// </summary>
/// <remarks>
/// Most of Bun's file exercises its generic, per-step item contract (arbitrary <c>Command</c>/
/// <c>Arguments</c>/<c>WorkingDirectory</c>/<c>ContinueOnError</c>, and a per-item incremental
/// <c>Inputs</c>/<c>Outputs</c>/<c>StampFile</c> triple) - none of which exists for Sass, whose
/// <c>SassBeforeStaticWebAssets</c> items describe fixed Sass entry points, not arbitrary steps. Only the
/// outcome-level scenarios that do not depend on that per-step contract are ported here: static web assets
/// packing, publish fingerprinting, the real <c>SassTimeoutMilliseconds</c> MSBuild property (as opposed to
/// setting <c>SassCompileTask.TimeoutMilliseconds</c> directly in-process, which
/// <see cref="SassCompileTaskIntegrationTests"/> already covers), and <c>SassClean</c>.
/// </remarks>
public class SassBeforeStaticWebAssetsTests
{
    private const string GeneratedAsset = "css/site.css";
    private const string TargetFramework = "net10.0";

    private readonly ITestOutputHelper _output;

    public SassBeforeStaticWebAssetsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SingleTargetFrameworkPack_IncludesGeneratedWwwrootFilesAsStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary();

        await Pack(workspace);

        // staticwebassets/ is what makes the file reachable as _content/<PackageId>/css/site.css in a
        // consuming app. An absolute Content glob still packs the file, but under content/ and
        // contentFiles/, where no consumer will ever serve it.
        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task RepeatedPack_DoesNotDuplicateGeneratedStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary();

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
            "Route"\s*:\s*"css/site\.[a-z0-9]+\.css"
            """.Trim()), manifest);
    }

    [Fact]
    public async Task SassTimeoutMillisecondsProperty_ShouldKillABuildThatOverruns()
    {
        // The real MSBuild-property counterpart to SassCompileTaskIntegrationTests' direct-task timeout
        // test: that one sets SassCompileTask.TimeoutMilliseconds in-process and never touches the .targets
        // file at all, so it cannot catch $(SassTimeoutMilliseconds) being dropped on the way to the task.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <SassTimeoutMilliseconds>200</SassTimeoutMilliseconds>
            <SassAdditionalArguments>--watch --poll</SassAdditionalArguments>
            """);

        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("timed out after 200ms", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_RemovesTheGeneratedStampAndManifest()
    {
        using var workspace = CreateRazorClassLibrary();

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var stampDirectory = workspace.PathTo("obj", DotnetCli.Configuration, TargetFramework, "Scarlet.Sass");
        var stamp = Path.Combine(stampDirectory, "Sass.settings.stamp");
        var manifest = Path.Combine(stampDirectory, "Sass.generated.txt");
        Assert.True(File.Exists(stamp), $"Expected a settings stamp at {stamp}.");
        Assert.True(File.Exists(manifest), $"Expected a generated-file manifest at {manifest}.");

        var clean = await RunDotnet(workspace, $"clean --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, clean.ExitCode);

        Assert.False(File.Exists(stamp), "dotnet clean should remove the settings stamp.");
        Assert.False(File.Exists(manifest), "dotnet clean should remove the generated-file manifest.");
    }

    /// <summary>
    /// A Razor Class Library is the strictest case: its wwwroot files have to be packed under
    /// staticwebassets/ for a consuming app to serve them.
    /// </summary>
    private TempWorkspace CreateRazorClassLibrary(string additionalProperties = "")
    {
        var workspace = TempWorkspace.Create("static-web-assets");

        try
        {
            workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");
            workspace.WriteFile(
                "GeneratedAssetsRcl.csproj",
                Project("Microsoft.NET.Sdk.Razor", additionalProperties));

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
            workspace.WriteFile("Sass/site.scss", ".banner { color: red; }");
            workspace.WriteFile(
                "Program.cs",
                "WebApplication.CreateBuilder(args).Build().Run();");
            workspace.WriteFile(
                "GeneratedAssetsWeb.csproj",
                Project("Microsoft.NET.Sdk.Web", additionalProperties: "<ImplicitUsings>enable</ImplicitUsings>"));

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static string Project(string sdk, string additionalProperties) =>
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
            <SassBeforeStaticWebAssets Include="Sass">
              <OutputPath>wwwroot/css</OutputPath>
            </SassBeforeStaticWebAssets>
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
