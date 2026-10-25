using System.IO.Compression;
using System.Security;
using System.Text.Json;
using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

/// <summary>
/// Multi-targeted libraries, built the ways that reach their per-framework (inner) builds without going
/// through the library's own outer build first: a referencing app, <c>dotnet build -f</c>, and a rebuild, whose
/// Clean phase evaluates the inner builds before anything is compiled.
/// </summary>
/// <remarks>
/// These install the packaged targets from a private feed rather than importing the development copy. The
/// development copy depends on ResolveProjectReferences, which a multi-targeting outer build does not have.
/// The package comes from <see cref="PackedTargetsFixture"/>, built and packed once, never from shared build
/// output. The class still runs alone because that fixture builds the task project, which every test importing
/// the development copy rebuilds through its ProjectReference.
/// </remarks>
[Collection(nameof(SassMultiTargetTests))]
[CollectionDefinition(nameof(SassMultiTargetTests), DisableParallelization = true)]
public class SassMultiTargetTests : IClassFixture<PackedTargetsFixture>
{
    private const string Library = "Rcl";

    private static readonly string RuntimeDirectory =
        Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Sass.MSBuild", "bin", "runtimes");

    private static readonly string[] LibraryFrameworks = ["net8.0", "net9.0", "net10.0"];

    private readonly ITestOutputHelper _output;
    private readonly PackedTargetsFixture _package;

    public SassMultiTargetTests(ITestOutputHelper output, PackedTargetsFixture package)
    {
        _output = output;
        _package = package;
    }

    [Fact]
    public async Task Rebuild_AfterDeletingTheOutput_NoBuildPackRetainsTheAsset()
    {
        using var workspace = await CreateWorkspace("multi-target-rebuild");
        var build = $"build Apps.slnx -c {DotnetCli.Configuration} --no-restore -v:n";

        Assert.Equal(1, await RunAndCountCompilations(workspace, build));

        File.Delete(LibraryPath(workspace, "wwwroot", "css", "site.css"));

        // Clean evaluates and caches the inner builds while site.css is missing; Build then compiles it in the
        // outer build, which cannot change the item lists those cached inner builds already hold.
        Assert.Equal(1, await RunAndCountCompilations(workspace, build + " --no-incremental"));
        Assert.True(new FileInfo(LibraryPath(workspace, "wwwroot", "css", "site.css")).Length > 0);

        // Inspect immediately: another build or a building pack can repair the broken manifests.
        var pack = await RunDotnet(workspace,
            $"pack {Library}/{Library}.csproj -c {DotnetCli.Configuration} --no-build -o nupkg -v:n");
        Assert.Equal(0, pack.ExitCode);
        Assert.Equal(0, CountCompilations(pack));

        using var package = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(workspace.PathTo("nupkg"), "*.nupkg")));
        var entry = Assert.Single(package.Entries, entry => entry.FullName == "staticwebassets/css/site.css");
        Assert.True(entry.Length > 0);

        foreach (var tfm in LibraryFrameworks)
            await AssertManifestHasAsset(LibraryManifest(workspace, tfm), "css/site.css");
    }

    /// <summary>
    /// <c>dotnet run --project App</c> builds a referenced multi-targeted library only through the app's
    /// ProjectReference, which addresses one inner build directly and never the library's outer build.
    /// </summary>
    [Fact]
    public async Task LibraryBuiltThroughProjectReferences_CompilesOnceAndAppsGetTheAsset()
    {
        using var workspace = await CreateWorkspace("multi-target-project-reference");

        // Only the app is built, as dotnet run does.
        Assert.Equal(1, await RunAndCountCompilations(workspace,
            $"build App10/App10.csproj -c {DotnetCli.Configuration} --no-restore -v:n"));
        await AssertManifestHasAsset(AppManifest(workspace, "App10", "net10.0"), "css/site.css");

        // The whole solution: the library's outer build, three inner builds and two apps all reach the
        // compilation, and between them must run it once.
        DeleteGeneratedCss(workspace);
        Assert.Equal(1, await RunAndCountCompilations(workspace,
            $"build Apps.slnx -c {DotnetCli.Configuration} --no-restore -v:n"));
        await AssertManifestHasAsset(AppManifest(workspace, "App10", "net10.0"), "css/site.css");
        await AssertManifestHasAsset(AppManifest(workspace, "App8", "net8.0"), "css/site.css");
        foreach (var tfm in LibraryFrameworks)
            await AssertManifestHasAsset(LibraryManifest(workspace, tfm), "css/site.css");
    }

    /// <summary>
    /// Unlike a plain glob, the task deletes outputs it no longer produces - in Sass, as soon as a stylesheet
    /// leaves the compiled directory. An inner build reached through a ProjectReference evaluates its wwwroot glob
    /// before asking the outer build to compile, so the glob can still hold a file the compilation has just
    /// deleted, which the static web assets SDK rejects with "No file exists for the asset".
    /// </summary>
    [Fact]
    public async Task DeletedStylesheet_IsDroppedFromAnInnerBuildThatSawItsCss()
    {
        using var workspace = await CreateWorkspace("multi-target-removed");
        workspace.WriteFile($"{Library}/Sass/extra.scss", ".extra { color: blue; }");

        Assert.Equal(0, (await RunDotnet(workspace,
            $"build Apps.slnx -c {DotnetCli.Configuration} --no-restore")).ExitCode);
        Assert.True(File.Exists(LibraryPath(workspace, "wwwroot", "css", "extra.css")));

        File.Delete(LibraryPath(workspace, "Sass", "extra.scss"));

        var appBuild = await RunDotnet(workspace,
            $"build App10/App10.csproj -c {DotnetCli.Configuration} --no-restore -v:n");
        Assert.Equal(0, appBuild.ExitCode);
        Assert.Equal(1, CountCompilations(appBuild));
        Assert.False(File.Exists(LibraryPath(workspace, "wwwroot", "css", "extra.css")));
        await AssertManifestHasAsset(AppManifest(workspace, "App10", "net10.0"), "css/site.css");
        await AssertManifestLacksAsset(AppManifest(workspace, "App10", "net10.0"), "css/extra.css");
    }

    /// <summary>
    /// Two instances of the library in one build - two callers passing different global properties - each hold a
    /// wwwroot glob evaluated while extra.css still existed. The first compilation deletes it; the second finds a
    /// manifest that no longer lists it, so unless removals outlive the compilation that made them, the second
    /// instance keeps the deleted file as Content and fails with "No file exists for the asset".
    /// </summary>
    [Fact]
    public async Task DeletedStylesheet_IsDroppedByEveryInstanceThatSawItsCss()
    {
        using var workspace = await CreateWorkspace("multi-target-deleted-variants");
        workspace.WriteFile($"{Library}/Sass/extra.scss", ".extra { color: blue; }");
        Assert.Equal(0, (await RunDotnet(workspace, $"build Apps.slnx -c {DotnetCli.Configuration} --no-restore")).ExitCode);
        Assert.True(File.Exists(LibraryPath(workspace, "wwwroot", "css", "extra.css")));

        File.Delete(LibraryPath(workspace, "Sass", "extra.scss"));
        workspace.WriteFile("TwoInstances.proj", TwoInstancesDriver($"{Library}/{Library}.csproj", "TargetFramework=net10.0;"));

        var build = await RunDotnet(workspace, $"msbuild TwoInstances.proj -p:Configuration={DotnetCli.Configuration} -v:n");

        Assert.Equal(0, build.ExitCode);
        Assert.Equal(2, CountCompilations(build));
        Assert.False(File.Exists(LibraryPath(workspace, "wwwroot", "css", "extra.css")));
    }

    /// <summary>
    /// A build that first evaluates <paramref name="project"/> as two instances, A and B, while every output still
    /// exists - as two parallel callers would - and only then resolves each one's static web assets, in order.
    /// </summary>
    internal static string TwoInstancesDriver(string project, string extraProperties) => $"""
        <Project>
          <Target Name="Build">
            <MSBuild Projects="{project}" Targets="GetTargetPath" Properties="{extraProperties}Variant=A" />
            <MSBuild Projects="{project}" Targets="GetTargetPath" Properties="{extraProperties}Variant=B" />
            <MSBuild Projects="{project}" Targets="ResolveProjectStaticWebAssets" Properties="{extraProperties}Variant=A" />
            <MSBuild Projects="{project}" Targets="ResolveProjectStaticWebAssets" Properties="{extraProperties}Variant=B" />
          </Target>
        </Project>
        """;

    [Fact]
    public async Task SingleFrameworkBuild_CompilesAndDesignTimeBuild_DoesNot()
    {
        using var workspace = await CreateWorkspace("multi-target-single-framework");
        var library = $"{Library}/{Library}.csproj";

        // Visual Studio's per-framework design-time builds reach static asset discovery through
        // CollectCopyToOutputDirectoryItemDesignTime -> _GetCopyToOutputDirectoryItemsFromThisProject ->
        // AssignTargetPaths, on project load. They must not start Sass.
        Assert.Equal(0, await RunAndCountCompilations(workspace,
            $"msbuild {library} -t:_GetCopyToOutputDirectoryItemsFromThisProject -v:n " +
            $"-p:Configuration={DotnetCli.Configuration} -p:TargetFramework=net8.0 " +
            "-p:DesignTimeBuild=true -p:BuildingInsideVisualStudio=true"));
        Assert.False(File.Exists(LibraryPath(workspace, "wwwroot", "css", "site.css")));

        // dotnet build -f reaches one inner build without the outer one, which still has to compile.
        Assert.Equal(1, await RunAndCountCompilations(workspace,
            $"build {library} -c {DotnetCli.Configuration} --no-restore -f net8.0 -v:n"));
        await AssertManifestHasAsset(LibraryManifest(workspace, "net8.0"), "css/site.css");
    }

    /// <summary>
    /// A solution holding the multi-targeted library and two apps referencing it, restored against the
    /// packaged targets, with no generated CSS on disk.
    /// </summary>
    private async Task<TempWorkspace> CreateWorkspace(string name)
    {
        var workspace = TempWorkspace.Create(name);

        try
        {
            // A private package cache ensures each test installs the targets just packed, even on repeated runs.
            workspace.WriteFile("nuget.config", $"""
                <configuration>
                  <config><add key="globalPackagesFolder" value="packages" /></config>
                  <packageSources>
                    <clear />
                    <add key="local" value="{SecurityElement.Escape(_package.FeedDirectory)}" />
                    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            workspace.WriteFile($"{Library}/Sass/site.scss", ".banner { color: red; }");
            workspace.WriteFile($"{Library}/{Library}.csproj", $"""
                <Project Sdk="Microsoft.NET.Sdk.Razor">
                  <PropertyGroup>
                    <TargetFrameworks>{string.Join(';', LibraryFrameworks)}</TargetFrameworks>
                    <SassRuntimeDirectory>{SecurityElement.Escape(RuntimeDirectory)}</SassRuntimeDirectory>
                  </PropertyGroup>
                  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
                  <ItemGroup><PackageReference Include="Scarlet.Sass.MSBuild" Version="{PackedTargetsFixture.Version}" PrivateAssets="all" /></ItemGroup>
                  <ItemGroup>
                    <SassBeforeStaticWebAssets Include="Sass">
                      <OutputPath>wwwroot/css</OutputPath>
                    </SassBeforeStaticWebAssets>
                  </ItemGroup>
                </Project>
                """);

            foreach (var (app, tfm) in new[] { ("App10", "net10.0"), ("App8", "net8.0") })
            {
                workspace.WriteFile($"{app}/{app}.csproj", $"""
                    <Project Sdk="Microsoft.NET.Sdk.Web">
                      <PropertyGroup>
                        <TargetFramework>{tfm}</TargetFramework>
                        <ImplicitUsings>enable</ImplicitUsings>
                      </PropertyGroup>
                      <ItemGroup><ProjectReference Include="..\{Library}\{Library}.csproj" /></ItemGroup>
                    </Project>
                    """);
                workspace.WriteFile($"{app}/Program.cs", "WebApplication.Create(args).Run();");
            }

            Assert.Equal(0, (await RunDotnet(workspace, "new sln -n Apps")).ExitCode);
            Assert.Equal(0, (await RunDotnet(workspace,
                $"sln add {Library}/{Library}.csproj App10/App10.csproj App8/App8.csproj")).ExitCode);
            Assert.Equal(0, (await RunDotnet(workspace, "restore Apps.slnx")).ExitCode);

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static string LibraryPath(TempWorkspace workspace, params string[] segments) =>
        workspace.PathTo([Library, .. segments]);

    private static string LibraryManifest(TempWorkspace workspace, string tfm) =>
        LibraryPath(workspace, "obj", DotnetCli.Configuration, tfm, "staticwebassets.build.json");

    private static string AppManifest(TempWorkspace workspace, string app, string tfm) =>
        workspace.PathTo(app, "obj", DotnetCli.Configuration, tfm, "staticwebassets.build.json");

    private static void DeleteGeneratedCss(TempWorkspace workspace)
    {
        var css = LibraryPath(workspace, "wwwroot", "css");
        if (Directory.Exists(css))
            Directory.Delete(css, recursive: true);
    }

    private async Task<int> RunAndCountCompilations(TempWorkspace workspace, string arguments)
    {
        var result = await RunDotnet(workspace, arguments);
        Assert.Equal(0, result.ExitCode);

        return CountCompilations(result);
    }

    /// <summary>
    /// One <c>Executing:</c> line per Dart Sass process - and these projects have one settings group, so one
    /// process per compilation. Needs at least normal verbosity.
    /// </summary>
    private static int CountCompilations(DotnetResult result) =>
        result.Output.Split('\n').Count(line => line.Contains("Executing: ", StringComparison.Ordinal));

    /// <summary>
    /// The manifest carries the library's asset once, as a non-empty primary asset - also when the manifest is a
    /// referencing app's.
    /// </summary>
    private static async Task AssertManifestHasAsset(string manifestPath, string relativePath)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var asset = Assert.Single(PrimaryLibraryAssets(manifest), item =>
            item.GetProperty("OriginalItemSpec").GetString()?.Replace('\\', '/') == $"wwwroot/{relativePath}");
        Assert.True(asset.GetProperty("FileLength").GetInt64() > 0);
    }

    private static async Task AssertManifestLacksAsset(string manifestPath, string relativePath)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.DoesNotContain(PrimaryLibraryAssets(manifest), item =>
            item.GetProperty("OriginalItemSpec").GetString()?.Replace('\\', '/').EndsWith(relativePath, StringComparison.Ordinal) == true);
    }

    private static IEnumerable<JsonElement> PrimaryLibraryAssets(JsonDocument manifest) =>
        manifest.RootElement.GetProperty("Assets").EnumerateArray().Where(item =>
            item.GetProperty("SourceId").GetString() == Library &&
            item.GetProperty("AssetRole").GetString() == "Primary");

    private async Task<DotnetResult> RunDotnet(TempWorkspace workspace, string arguments)
    {
        var result = await DotnetCli.Run(workspace.RootDirectory, arguments);
        _output.WriteLine(result.Output);

        return result;
    }
}
