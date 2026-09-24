using System.IO.Compression;
using System.Security;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

/// <summary>
/// Covers the <c>RunSassBeforeStaticWebAssets</c> target end to end, by running real <c>dotnet build</c>,
/// <c>pack</c> and <c>publish</c> invocations and asserting on what they produce.
/// </summary>
/// <remarks>
/// These tests focus on outcomes that only a real SDK build can prove: static web assets packing, publish
/// fingerprinting, the real <c>SassTimeoutMilliseconds</c> MSBuild property (as opposed to setting
/// <c>SassCompileTask.TimeoutMilliseconds</c> directly in-process, which
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
    public async Task FailingSass_FailsTheBuildByDefault()
    {
        using var workspace = CreateRazorClassLibrary();
        workspace.WriteFile("Sass/site.scss", "$broken: ;");

        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Sass command failed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SassStampDirectoryProperty_ShouldPutTheStampAndManifestWhereItAsks()
    {
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <SassStampDirectory>custom/stamps</SassStampDirectory>
            """);

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var stamp = workspace.PathTo("custom", "stamps", "Sass.settings.stamp");
        var manifest = workspace.PathTo("custom", "stamps", "Sass.generated.txt");
        Assert.True(File.Exists(stamp), $"Expected a settings stamp at {stamp}.");
        Assert.True(File.Exists(manifest), $"Expected a generated-file manifest at {manifest}.");

        var defaultStampDirectory = workspace.PathTo("obj", DotnetCli.Configuration, TargetFramework, "Scarlet.Sass");
        Assert.False(File.Exists(Path.Combine(defaultStampDirectory, "Sass.settings.stamp")));
        Assert.False(File.Exists(Path.Combine(defaultStampDirectory, "Sass.generated.txt")));

        var clean = await RunDotnet(workspace, $"clean --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, clean.ExitCode);

        Assert.False(File.Exists(stamp), "dotnet clean should remove the requested settings stamp.");
        Assert.False(File.Exists(manifest), "dotnet clean should remove the requested generated-file manifest.");
    }

    [Fact]
    public async Task ConfigurationProperty_ShouldDriveTheDebugAndReleaseDefaults()
    {
        // $(Configuration) is the one task input whose C# default ("Debug") agrees with what most tests
        // build, so dropping Configuration="$(Configuration)" from the .targets changed nothing observable
        // and no test noticed. Unwired, the task always believes it is a Debug build, and a Release build
        // silently ships expanded CSS with a source map.
        //
        // The spawned build has to use the same configuration as this assembly - the development targets
        // resolve the task from bin\$(Configuration)\netstandard2.0 - so this asserts whichever half applies
        // to the current run. CI builds Release, which is the half that matters: there, a dropped wiring
        // makes the task fall back to Debug and this fails.
        using var workspace = CreateRazorClassLibrary();

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var css = File.ReadAllText(workspace.PathTo("wwwroot", "css", "site.css"));
        var sourceMap = workspace.PathTo("wwwroot", "css", "site.css.map");

        if (string.Equals(DotnetCli.Configuration, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Contains("\n", css, StringComparison.Ordinal);
            Assert.True(File.Exists(sourceMap), "A Debug build should emit a source map.");
        }
        else
        {
            // Compressed output is a single line, so the only newline Dart Sass writes is the trailing one.
            Assert.DoesNotContain("\n", css.TrimEnd());
            Assert.False(File.Exists(sourceMap), "A Release build should not emit a source map.");
        }
    }

    [Fact]
    public async Task SassOutputStyleAndSourceMapProperties_ShouldOverrideTheConfigurationDefaults()
    {
        // Proves both properties survive the trip through the .targets, and that an explicit setting beats
        // whatever the configuration would otherwise imply - which is the whole point of them being settable.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <SassOutputStyle>Compressed</SassOutputStyle>
            <SassSourceMap>false</SassSourceMap>
            """);

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var css = File.ReadAllText(workspace.PathTo("wwwroot", "css", "site.css"));
        Assert.DoesNotContain("\n", css.TrimEnd());
        Assert.False(
            File.Exists(workspace.PathTo("wwwroot", "css", "site.css.map")),
            "SassSourceMap=false should suppress the source map even in a Debug build.");
    }

    [Fact]
    public async Task SassEmbedSourcesProperty_ShouldInlineTheOriginalStylesheetsIntoTheSourceMap()
    {
        // Like the Configuration test, this bites in Release: EmbedSources defaults to false there, so a
        // dropped wiring leaves sourcesContent out and this fails. In Debug the default already matches.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <SassSourceMap>true</SassSourceMap>
            <SassEmbedSources>true</SassEmbedSources>
            """);

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var sourceMap = workspace.PathTo("wwwroot", "css", "site.css.map");
        Assert.True(File.Exists(sourceMap), "SassSourceMap=true should emit a source map.");
        Assert.Contains("sourcesContent", File.ReadAllText(sourceMap), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SassLoadPathsProperty_ShouldLetStylesheetsResolveOutsideTheirOwnDirectory()
    {
        // A load path is only observable through a stylesheet that cannot compile without it, so this is
        // also the assertion that the property is reaching Dart Sass rather than being silently ignored.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <SassLoadPaths>Shared</SassLoadPaths>
            """);
        // A length rather than a colour: compressed output rewrites #663399 to #639, so asserting on a hex
        // literal would pass in Debug and fail in Release for reasons that have nothing to do with load paths.
        workspace.WriteFile("Shared/_tokens.scss", "$gap: 12.5px;");
        workspace.WriteFile("Sass/site.scss", """
            @use "tokens";
            .banner { padding: tokens.$gap; }
            """);

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");

        // The exit code carries most of the claim: without the load path reaching Dart Sass, @use "tokens"
        // cannot resolve and the build fails outright.
        Assert.Equal(0, build.ExitCode);
        Assert.Contains("12.5px", File.ReadAllText(workspace.PathTo("wwwroot", "css", "site.css")), StringComparison.Ordinal);
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
