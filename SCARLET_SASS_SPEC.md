# Scarlet.Sass Specification

Status: draft  
Audience: package maintainer, future contributors, AI agents  
Scope: MSBuild and CLI integration for Dart Sass in .NET projects

## Summary

`Scarlet.Sass` should provide pinned Dart Sass execution for .NET builds and repository-local command
line use, with the same build correctness priorities as `Scarlet.Bun`: multi-targeting, Razor Class
Library support, static web assets, deterministic runtime resolution, strong tests, and clear diagnostics.

The package should not start as a general .NET Sass host library. It should not register ASP.NET Core
services, expose a dependency injection compiler API, or invent an appsettings/json configuration dialect.
The primary API should be MSBuild properties and items, plus a thin `dotnet sass` CLI that forwards Dart Sass
arguments.

## Goals

- Compile `.scss` and `.sass` files during `dotnet build`.
- Work in ASP.NET Core apps, Blazor apps, and Razor Class Libraries.
- Work correctly in single-TFM and multi-TFM projects.
- Run Sass before static web asset discovery so generated `wwwroot` files are discovered by the SDK.
- Support generated CSS isolation inputs, for example `Component.razor.scss` to `Component.razor.css`.
- Package generated RCL assets under `staticwebassets/` when the project is packed.
- Support `dotnet pack --no-build` after a previous build without re-running Sass.
- Support `dotnet clean` for generated CSS, source maps, manifests, and cache/stamp files.
- Provide pinned Dart Sass binaries through platform-specific NuGet runtime packages.
- Provide optional download-on-demand mode for environments that prefer not to reference runtime packages.
- Provide a repository-pinned .NET tool for command-line use.
- Keep configuration in MSBuild, not JSON.
- Make all important behavior testable with unit, integration, and e2e tests.

## Non-Goals

- Do not provide an ASP.NET Core middleware or runtime compiler in v1.
- Do not provide `services.AddSassCompiler()` from the MSBuild package.
- Do not compete with EmbeddedSass.Net as a public compiler host or DI library.
- Do not implement custom Sass importers or custom Sass functions in v1.
- Do not implement the Embedded Sass Protocol in v1 unless measurement proves the Dart Sass CLI approach is
  insufficient.
- Do not create a `sasscompiler.json`, `appsettings.json`, or any other Scarlet-specific configuration file
  convention. If Dart Sass itself does not support a configuration file, Scarlet.Sass should not invent one.

## Package Layout

Recommended packages:

| Package | Purpose | Versioning |
| --- | --- | --- |
| `Scarlet.Sass.MSBuild` | MSBuild task, props, and targets | Scarlet-controlled package version |
| `Scarlet.Sass.Cli` | `dotnet sass` tool pointer package | Dart Sass version, optionally with Scarlet revision |
| `Scarlet.Sass.Runtime.windows-x64` | Dart Sass for Windows x64 | Dart Sass version |
| `Scarlet.Sass.Runtime.windows-arm64` | Dart Sass for Windows ARM64 | Dart Sass version |
| `Scarlet.Sass.Runtime.linux-x64` | Dart Sass for Linux x64 glibc | Dart Sass version |
| `Scarlet.Sass.Runtime.linux-arm64` | Dart Sass for Linux ARM64 glibc | Dart Sass version |
| `Scarlet.Sass.Runtime.linux-x64-musl` | Dart Sass for Linux x64 musl | Dart Sass version |
| `Scarlet.Sass.Runtime.linux-arm64-musl` | Dart Sass for Linux ARM64 musl | Dart Sass version |
| `Scarlet.Sass.Runtime.darwin-x64` | Dart Sass for macOS x64 | Dart Sass version |
| `Scarlet.Sass.Runtime.darwin-arm64` | Dart Sass for macOS ARM64 | Dart Sass version |

Optional later package:

| Package | Purpose |
| --- | --- |
| `Scarlet.Sass.Embedded` | Experimental or advanced Embedded Sass Protocol host implementation |

The runtime package split should follow the `Scarlet.Bun.Runtime.*` model: the build host decides which
runtime is needed. The project's target RID is not the right source of truth because Sass runs on the build
host, not on the target deployment platform.

## Runtime Discovery Contract

Runtime packages should contribute an item to both `build/` and `buildMultiTargeting/`:

```xml
<ItemGroup>
  <SassRuntimePack Include="Scarlet.Sass.Runtime.linux-x64">
    <Rid>linux-x64</Rid>
    <RuntimesPath>$(MSBuildThisFileDirectory)..\runtimes</RuntimesPath>
    <Variant>default</Variant>
    <Priority>0</Priority>
  </SassRuntimePack>
</ItemGroup>
```

The MSBuild task should accept `@(SassRuntimePack)` and resolve the current host runtime. The item contract
should be the only runtime package contract in v1. There is no need to create a legacy property contract
like `SassRuntime_linux_x64` unless there is an already-published compatibility burden.

Runtime layout should follow NuGet native asset conventions:

```text
runtimes/
  linux-x64/
    native/
      sass
```

Windows uses `sass.bat`, `sass.exe`, or the executable name supplied by the Dart Sass release. The resolver
should hide that detail behind per-platform metadata.

## MSBuild API

The primary build item should mirror `Scarlet.Bun` naming for the static web assets scenario:
`SassBeforeStaticWebAssets`.

That name is intentionally specific. Most users want Sass output to become Blazor/Razor static web assets,
so the public item should describe when the work runs and why it exists. A lower-level `Sass` target can
still exist for explicit task calls, but the documented path should be `SassBeforeStaticWebAssets`.

### Minimal Usage

There should be no implicit `Sass/` folder convention in v1. Consumers should opt in with explicit
`@(SassBeforeStaticWebAssets)` items so the build graph is visible and testable.

```xml
<ItemGroup>
  <PackageReference Include="Scarlet.Sass.MSBuild" Version="1.0.0" PrivateAssets="all" />
  <PackageReference Include="Scarlet.Sass.Runtime.windows-x64" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
```

Explicit usage should be preferred in documentation:

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass">
    <OutputPath>wwwroot/css</OutputPath>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

Directory input means "compile all entry point files in this directory tree." Entry points are `.scss` and
`.sass` files whose filename does not start with `_`.

### File-to-File Usage

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass/site.scss">
    <OutputPath>wwwroot/css/site.css</OutputPath>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

### Directory-to-Directory Usage

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass">
    <OutputPath>wwwroot/css</OutputPath>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

This should map to Dart Sass many-to-many mode:

```bash
sass Sass:wwwroot/css
```

### Multiple Inputs

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass">
    <OutputPath>wwwroot/css</OutputPath>
  </SassBeforeStaticWebAssets>

  <SassBeforeStaticWebAssets Include="Themes/admin.scss">
    <OutputPath>wwwroot/css/admin.css</OutputPath>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

The task should prefer one Dart Sass process per build target invocation, not one process per file.

## Properties

Recommended properties:

Public MSBuild properties should use the `Sass*` prefix for consistency with `Scarlet.Bun` properties such
as `SassRuntimeDownload` and `SassVersionDownload`. Environment variables should keep the more explicit
`SCARLET_SASS_*` prefix to avoid collisions with other Sass tooling.

```xml
<PropertyGroup>
  <SassEnabled>true</SassEnabled>
  <SassOutputStyle>Auto</SassOutputStyle>
  <SassSourceMap>Auto</SassSourceMap>
  <SassEmbedSources>Auto</SassEmbedSources>
  <SassQuietDeps>false</SassQuietDeps>
  <SassSilenceDeprecations></SassSilenceDeprecations>
  <SassFatalDeprecations></SassFatalDeprecations>
  <SassLoadPaths></SassLoadPaths>
  <SassPkgImporter></SassPkgImporter>
  <SassAdditionalArguments></SassAdditionalArguments>
  <SassRuntimeDirectory></SassRuntimeDirectory>
  <SassRuntimeDownload>false</SassRuntimeDownload>
  <SassVersionDownload></SassVersionDownload>
  <SassDownloadMutexTimeoutSeconds>300</SassDownloadMutexTimeoutSeconds>
</PropertyGroup>
```

Suggested default behavior:

| Property | Debug default | Release default |
| --- | --- | --- |
| `SassOutputStyle=Auto` | `expanded` | `compressed` |
| `SassSourceMap=Auto` | `true` | `false` |
| `SassEmbedSources=Auto` | `true` | `false` |

`SassAdditionalArguments` should exist as an escape hatch, but first-class properties should cover
common Dart Sass flags so builds stay readable and testable.

## Item Metadata

Recommended item metadata:

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass/site.scss">
    <OutputPath>wwwroot/css/site.css</OutputPath>
    <LoadPaths>Sass;node_modules</LoadPaths>
    <OutputStyle>Compressed</OutputStyle>
    <SourceMap>false</SourceMap>
    <EmbedSources>false</EmbedSources>
    <QuietDeps>true</QuietDeps>
    <SilenceDeprecations></SilenceDeprecations>
    <FatalDeprecations></FatalDeprecations>
    <AdditionalArguments></AdditionalArguments>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

Metadata overrides global properties for that item.

## Load Paths

`LoadPaths` are a Sass primitive, exposed by Dart Sass as `--load-path` or `-I`. They are not a Scarlet
invention and are not equivalent to anything in `Scarlet.Bun`.

Sass uses load paths to resolve `@use`, `@forward`, and legacy `@import` references that are not relative
to the current file.

Example:

```scss
@use "tokens/colors";
@use "bootstrap/scss/bootstrap";
```

With:

```xml
<SassLoadPaths>Sass;node_modules</SassLoadPaths>
```

Sass can find:

```text
Sass/tokens/_colors.scss
node_modules/bootstrap/scss/bootstrap.scss
```

`LoadPaths` should be included because many Sass projects rely on them, especially Bootstrap-style setups.
They should not be confused with JavaScript module resolution, which is handled by Bun/Node/package tools in
`Scarlet.Bun`.

## Node Package Importer

Dart Sass supports a Node package importer via `--pkg-importer=node`. Scarlet.Sass should expose it without
requiring Node.js to execute Sass. This option affects resolution of `pkg:` URLs according to Node package
resolution rules.

Possible API:

```xml
<PropertyGroup>
  <SassPkgImporter>node</SassPkgImporter>
</PropertyGroup>
```

Or per item:

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass/site.scss">
    <OutputPath>wwwroot/css/site.css</OutputPath>
    <PkgImporter>node</PkgImporter>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

## Static Web Assets

The main target should be named `RunSassBeforeStaticWebAssets`, matching the existing
`BunBeforeStaticWebAssets` idea from Scarlet.Bun. It should run when `@(SassBeforeStaticWebAssets)` is present:

```xml
<Target Name="RunSassBeforeStaticWebAssets"
        BeforeTargets="DispatchToInnerBuilds;ResolveProjectStaticWebAssets;PreBuildEvent"
        Condition="'$(SassEnabled)' == 'true'
                   AND '@(SassBeforeStaticWebAssets)' != ''
                   AND '$(DesignTimeBuild)' != 'true'
                   AND '$(NoBuild)' != 'true'
                   AND ('$(TargetFrameworks)' == '' OR '$(TargetFramework)' == '')">
  <!-- task call -->
</Target>
```

The important behavior:

- In single-TFM projects, run before `ResolveProjectStaticWebAssets`.
- In multi-TFM projects, run once in the outer build before `DispatchToInnerBuilds`.
- Do not run during `dotnet pack --no-build`.
- Generated files under `wwwroot` must be added to `@(Content)` if they were produced after project
  evaluation.
- Avoid duplicate `@(Content)` entries on incremental builds.
- Generated files should be added to `@(FileWrites)` when possible so `dotnet clean` can remove them.

The package must include both:

```text
build/Scarlet.Sass.MSBuild.props
build/Scarlet.Sass.MSBuild.targets
buildMultiTargeting/Scarlet.Sass.MSBuild.props
buildMultiTargeting/Scarlet.Sass.MSBuild.targets
```

`buildMultiTargeting` is load-bearing for multi-TFM projects because the outer build has an empty
`$(TargetFramework)`.

## CSS Isolation

Scarlet.Sass should support a dedicated CSS isolation path:

```xml
<PropertyGroup>
  <SassCssIsolation>true</SassCssIsolation>
</PropertyGroup>
```

Suggested conventions:

| Input | Output |
| --- | --- |
| `Components/Button.razor.scss` | `Components/Button.razor.css` |
| `Pages/Home.cshtml.scss` | `Pages/Home.cshtml.css` |

The target for CSS isolation must run before the Razor SDK discovers scoped CSS files. This may need a
separate target from the `wwwroot` static web assets path. Tests should prove generated `.razor.css` files
are picked up by the Razor scoped CSS pipeline.

If this is risky for v1, ship `wwwroot` static web assets first and keep CSS isolation behind an explicit
property.

## Incremental Build Strategy

Use a hybrid incremental strategy.

Unlike `Scarlet.Bun`, Dart Sass is not an opaque general command. Dart Sass has native incremental behavior through
`--update`: it compiles only stylesheets whose dependencies are newer than the corresponding generated CSS
file. Scarlet.Sass should use that instead of rebuilding Sass dependency tracking from scratch.

However, Dart Sass does not know about MSBuild-level state. It will not automatically regenerate output when
only Scarlet/MSBuild settings change, such as:

- `SassOutputStyle`
- `SassSourceMap`
- `SassEmbedSources`
- `SassLoadPaths`
- `SassPkgImporter`
- deprecation and quiet dependency flags
- runtime package selection
- downloaded Dart Sass version
- explicit `SassRuntimeDirectory`

So the split should be:

- Dart Sass owns Sass dependency-aware compile freshness via `--update`.
- Scarlet.Sass owns MSBuild correctness around settings, runtime selection, generated file tracking, stale
  output deletion, clean, static web assets, and `pack --no-build`.

Recommended v1 behavior:

- Discover entry point files.
- Discover expected CSS and source map outputs.
- Include global and item-level settings in a stamp/cache key.
- Include runtime selection and resolved Dart Sass version in the stamp/cache key.
- Require every expected CSS output to exist before allowing a no-process skip.
- If the Scarlet stamp is valid, generated outputs exist, and no stale outputs need deletion, the task may
  skip launching Dart Sass entirely.
- If the Scarlet stamp is missing or stale, invoke Dart Sass once per task invocation with `--update` and
  many-to-many input/output pairs.
- When settings or runtime selection change, force Dart Sass to regenerate even if the Sass source tree did
  not change. This can be done by deleting expected CSS/map outputs before the `--update` run, by using a
  separate force mode, or by another implementation that guarantees fresh output.
- Record generated files in a manifest for clean and stale output deletion.
- Return generated files to MSBuild even when Dart Sass skips them internally, so static web asset item
  injection remains deterministic.

Avoid making target-level MSBuild `Inputs` / `Outputs` the primary mechanism. They are useful for simple
one-file transforms, but Sass has partials, load paths, directory-to-directory compilation, source maps,
stale outputs, and generated `@(Content)` items. A target-level up-to-date check would either be too coarse
or would duplicate Dart Sass's dependency tracking badly.

The result should be simpler than `BunBeforeStaticWebAssets`: Bun needs Scarlet-managed incrementality
because Bun commands are arbitrary. Sass should delegate stylesheet dependency freshness to Dart Sass and
keep Scarlet's incremental layer focused on the .NET build system contract.

## Cleaning

The package should remove:

- Generated `.css`.
- Generated `.css.map`.
- Generated CSS isolation `.razor.css` / `.cshtml.css` files.
- Generated manifest files.
- Generated stamp/cache files.
- Stale files that were generated by a previous build but no longer map to a current entry point.

Clean should be manifest-driven, not a broad delete of `wwwroot/css`.

## CLI API

`Scarlet.Sass.Cli` should be a .NET tool:

```bash
dotnet new tool-manifest
dotnet tool install Scarlet.Sass.Cli
dotnet sass Sass:wwwroot/css --style=compressed
```

The tool should forward arguments to Dart Sass verbatim.

Reserved Scarlet flags:

```bash
dotnet sass --scarlet-info
dotnet sass --scarlet-info --json
```

Like `Scarlet.Sass.Cli`, reserve only a branded diagnostics flag in the first position, with a permanent
environment variable opt-out:

```text
SCARLET_SASS_PASSTHROUGH=1
```

Useful environment variables:

```text
SCARLET_SASS_PATH
SCARLET_SASS_VERSION
SCARLET_SASS_CACHE_DIR
SCARLET_SASS_DIAGNOSTICS
SCARLET_SASS_PASSTHROUGH
```

CLI resolution order:

1. `SCARLET_SASS_PATH`
2. Embedded runtime in the RID-specific CLI package
3. Cache for requested Dart Sass version
4. Download-on-demand fallback, if the package design allows it

## JSON Configuration

Do not add JSON configuration.

`sasscompiler.json`-style configuration is a convention used by other .NET packages. It is not a Dart Sass
primitive. Dart Sass is configured through CLI flags and input/output pairs. Scarlet.Sass should use MSBuild
properties and items because they are visible to restore/build/pack/clean, compose with multi-targeting, and
are easier to test in e2e package scenarios.

This is not just a v1 deferral. Unless Dart Sass itself grows an official configuration file format,
Scarlet.Sass should not introduce one. A Scarlet-specific JSON file would become another dialect to document,
validate, migrate, and debug, while also hiding build inputs from normal MSBuild evaluation.

## Embedded Sass Protocol

### What It Is

The Embedded Sass Protocol is a host/compiler protocol. A host process starts an Embedded Sass compiler and
communicates with it using structured messages, rather than invoking the `sass` CLI and parsing process
output. It enables hosts to keep a compiler process alive and to implement advanced Sass features such as
custom importers and custom functions.

### What It Buys Scarlet.Sass

Potential benefits:

- A persistent compiler process across many compile requests.
- Lower warm compile latency for repeated compilations.
- Structured compiler diagnostics instead of CLI text parsing.
- A path to exact dependency information, if exposed by the protocol interactions.
- Custom importers written in .NET.
- Custom Sass functions written in .NET.
- A future watch/build-server mode that does not restart Sass for every change.

### What It Does Not Buy v1

For the v1 MSBuild task and CLI, it probably does not justify its cost.

If the task invokes Dart Sass once per outer build in many-to-many mode, process startup happens once per
build target invocation, not once per Sass file. That avoids the biggest performance trap while keeping the
implementation close to the official Dart Sass CLI.

The protocol would add:

- Protobuf schema management.
- Compiler process lifecycle management.
- Request/response correlation.
- Cancellation behavior.
- Importer and function callback plumbing.
- Compatibility testing against Dart Sass embedded releases.
- More package dependencies in the MSBuild task loading context.

Recommendation: design a backend abstraction so the protocol can be added later without changing the public
MSBuild API.

### Theoretical Backend Abstraction

The task should compile through an internal engine interface:

```csharp
internal interface ISassCompilerEngine : IAsyncDisposable
{
    Task<SassCompileBatchResult> CompileAsync(
        SassCompileBatchRequest request,
        CancellationToken cancellationToken);
}
```

Shared request model:

```csharp
internal sealed class SassCompileBatchRequest
{
    public required string ProjectDirectory { get; init; }
    public required IReadOnlyList<SassCompilationEntry> Entries { get; init; }
    public required SassCompilerSettings Settings { get; init; }
}

internal sealed class SassCompilationEntry
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public bool IsDirectory { get; init; }
    public IReadOnlyList<string> LoadPaths { get; init; } = Array.Empty<string>();
}

internal sealed class SassCompilerSettings
{
    public string OutputStyle { get; init; } = "expanded";
    public bool SourceMap { get; init; }
    public bool EmbedSources { get; init; }
    public bool QuietDeps { get; init; }
    public IReadOnlyList<string> SilenceDeprecations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FatalDeprecations { get; init; } = Array.Empty<string>();
    public string? PkgImporter { get; init; }
    public IReadOnlyList<string> AdditionalArguments { get; init; } = Array.Empty<string>();
}
```

CLI engine implementation:

```csharp
internal sealed class DartSassCliEngine : ISassCompilerEngine
{
    private readonly string _sassExecutablePath;
    private readonly IProcessLauncher _processLauncher;

    public DartSassCliEngine(string sassExecutablePath, IProcessLauncher processLauncher)
    {
        _sassExecutablePath = sassExecutablePath;
        _processLauncher = processLauncher;
    }

    public Task<SassCompileBatchResult> CompileAsync(
        SassCompileBatchRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DartSassArguments.From(request);
        return _processLauncher.RunAsync(_sassExecutablePath, arguments, request.ProjectDirectory, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Theoretical Embedded Sass Protocol implementation:

```csharp
internal sealed class EmbeddedSassProtocolEngine : ISassCompilerEngine
{
    private readonly EmbeddedSassConnection _connection;

    public EmbeddedSassProtocolEngine(EmbeddedSassConnection connection)
    {
        _connection = connection;
    }

    public async Task<SassCompileBatchResult> CompileAsync(
        SassCompileBatchRequest request,
        CancellationToken cancellationToken)
    {
        var outputs = new List<SassCompilationOutput>();

        foreach (var entry in request.Entries)
        {
            var protocolRequest = EmbeddedSassRequestMapper.Map(entry, request.Settings);
            var protocolResponse = await _connection.CompileAsync(protocolRequest, cancellationToken)
                                                    .ConfigureAwait(false);

            outputs.Add(EmbeddedSassResponseMapper.Map(entry, protocolResponse));
        }

        return new SassCompileBatchResult(outputs);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
```

The public MSBuild API stays the same:

```xml
<PropertyGroup>
  <SassEngine>Cli</SassEngine>
</PropertyGroup>
```

Experimental opt-in later:

```xml
<PropertyGroup>
  <SassEngine>EmbeddedProtocol</SassEngine>
</PropertyGroup>
```

This keeps `Scarlet.Sass.MSBuild` from committing to the protocol before it has evidence, while preserving
a clean extension point.

## MSBuild Task Shape

Possible task:

```csharp
public sealed class SassCompileTask : Microsoft.Build.Utilities.Task, ICancelableTask
{
    [Required]
    public ITaskItem[] Compilations { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Debug";
    public string OutputStyle { get; set; } = "Auto";
    public string SourceMap { get; set; } = "Auto";
    public string EmbedSources { get; set; } = "Auto";
    public string QuietDeps { get; set; } = "false";
    public string LoadPaths { get; set; } = string.Empty;
    public string PkgImporter { get; set; } = string.Empty;
    public string SilenceDeprecations { get; set; } = string.Empty;
    public string FatalDeprecations { get; set; } = string.Empty;
    public string AdditionalArguments { get; set; } = string.Empty;
    public string RuntimeDirectory { get; set; } = string.Empty;
    public bool RuntimeDownload { get; set; }
    public string VersionDownload { get; set; } = string.Empty;
    public ITaskItem[] RuntimePacks { get; set; } = Array.Empty<ITaskItem>();

    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = Array.Empty<ITaskItem>();

    [Output]
    public ITaskItem[] RemovedFiles { get; private set; } = Array.Empty<ITaskItem>();

    public void Cancel()
    {
        // Cancel process/protocol operation.
    }

    public override bool Execute()
    {
        // Resolve settings, discover entries, resolve runtime, compile, update manifests.
        return !Log.HasLoggedErrors;
    }
}
```

Task implementation rules:

- Inherit from `Microsoft.Build.Utilities.Task`.
- Return `false` on failure.
- Log actionable errors.
- Support cancellation.
- Avoid static mutable state.
- Use file system and process abstractions for tests.
- Pack every task dependency next to the task assembly.
- Keep the task target framework compatible with the intended MSBuild loading surface.

## Build Target Sketch

```xml
<Project>
  <UsingTask TaskName="Scarlet.Sass.MSBuild.SassCompileTask"
             AssemblyFile="$(MSBuildThisFileDirectory)..\tools\netstandard2.0\Scarlet.Sass.MSBuild.dll" />

  <PropertyGroup>
    <SassEnabled Condition="'$(SassEnabled)' == ''">true</SassEnabled>
    <SassOutputStyle Condition="'$(SassOutputStyle)' == ''">Auto</SassOutputStyle>
    <SassSourceMap Condition="'$(SassSourceMap)' == ''">Auto</SassSourceMap>
    <SassEmbedSources Condition="'$(SassEmbedSources)' == ''">Auto</SassEmbedSources>
    <SassQuietDeps Condition="'$(SassQuietDeps)' == ''">false</SassQuietDeps>
  </PropertyGroup>

  <Target Name="RunSassBeforeStaticWebAssets"
          BeforeTargets="DispatchToInnerBuilds;ResolveProjectStaticWebAssets;PreBuildEvent"
          Condition="'$(SassEnabled)' == 'true'
                     AND '@(SassBeforeStaticWebAssets)' != ''
                     AND '$(DesignTimeBuild)' != 'true'
                     AND '$(NoBuild)' != 'true'
                     AND ('$(TargetFrameworks)' == '' OR '$(TargetFramework)' == '')">
    <SassCompileTask Compilations="@(SassBeforeStaticWebAssets)"
                     ProjectDirectory="$(MSBuildProjectDirectory)"
                     Configuration="$(Configuration)"
                     OutputStyle="$(SassOutputStyle)"
                     SourceMap="$(SassSourceMap)"
                     EmbedSources="$(SassEmbedSources)"
                     QuietDeps="$(SassQuietDeps)"
                     LoadPaths="$(SassLoadPaths)"
                     PkgImporter="$(SassPkgImporter)"
                     SilenceDeprecations="$(SassSilenceDeprecations)"
                     FatalDeprecations="$(SassFatalDeprecations)"
                     AdditionalArguments="$(SassAdditionalArguments)"
                     RuntimeDirectory="$(SassRuntimeDirectory)"
                     RuntimeDownload="$(SassRuntimeDownload)"
                     VersionDownload="$(SassVersionDownload)"
                     RuntimePacks="@(SassRuntimePack)">
      <Output TaskParameter="GeneratedFiles" ItemName="_SassGeneratedFiles" />
      <Output TaskParameter="RemovedFiles" ItemName="_SassRemovedFiles" />
    </SassCompileTask>

    <ItemGroup>
      <Content Remove="@(_SassRemovedFiles)" />
      <None Remove="@(_SassRemovedFiles)" />
      <_SassGeneratedContent Include="%(_SassGeneratedFiles.RelativePath)" />
      <_SassNewContent Include="@(_SassGeneratedContent)" Exclude="@(Content)" />
      <Content Include="@(_SassNewContent)" CopyToPublishDirectory="PreserveNewest" />
      <FileWrites Include="@(_SassGeneratedFiles)" />
    </ItemGroup>
  </Target>
</Project>
```

This sketch is intentionally not final. It shows the shape and the static web asset integration points.

## Testing Requirements

Scarlet.Sass should start by borrowing the Scarlet.Bun sample, integration, and e2e structure, then adapt it
to Sass-specific behavior. The goal is not merely to have "some tests"; it should have the same kind of
package-consumer confidence Scarlet.Bun has:

- Samples that demonstrate the documented happy paths.
- Unit tests for task internals and target/property plumbing.
- Integration tests that execute a real Sass compiler.
- E2E tests that pack real NuGet packages into a local feed and consume them from fresh projects.

Every public MSBuild property and item metadata value should have at least one test that would fail if the
`.targets` file stopped passing it into the task. This is important because many regressions are a single
missing attribute, for example removing `StampFile="%(_BunBeforeStaticWebAssetsStep.StampFile)"` in
Scarlet.Bun. Scarlet.Sass should intentionally test that kind of wiring.

Samples:

- Basic ASP.NET Core or Blazor app compiling explicit Sass inputs into `wwwroot/css`.
- Razor Class Library sample where generated CSS is consumed through `_content/<PackageId>/...`.
- Multi-TFM Razor Class Library sample.
- Download-on-demand runtime sample.
- CLI sample using `dotnet sass`.
- Optional CSS isolation sample if `SassCssIsolation` ships.

Unit tests:

- Platform detection and RID mapping.
- Runtime pack parsing, deduplication, priority ordering.
- Runtime executable path resolution.
- Missing runtime diagnostics.
- Dart Sass argument rendering.
- `Auto` option resolution for Debug and Release.
- Entry point discovery and partial exclusion.
- `@(SassBeforeStaticWebAssets)` metadata parsing.
- Load path merging from global properties and item metadata.
- Manifest/cache/stamp behavior.
- Settings/runtime stamp invalidation that forces regeneration even when Sass sources are unchanged.
- Task dependency packaging.
- `.props` defaults for every public property.
- `.targets` task invocation includes every public property:
  - `SassEnabled`
  - `SassOutputStyle`
  - `SassSourceMap`
  - `SassEmbedSources`
  - `SassQuietDeps`
  - `SassLoadPaths`
  - `SassPkgImporter`
  - `SassSilenceDeprecations`
  - `SassFatalDeprecations`
  - `SassAdditionalArguments`
  - `SassRuntimeDirectory`
  - `SassRuntimeDownload`
  - `SassVersionDownload`
  - `SassDownloadMutexTimeoutSeconds`
  - `SassEngine`, if the backend selector ships.
  - `SassCssIsolation`, if CSS isolation ships.
- `.targets` task invocation includes every supported `@(SassBeforeStaticWebAssets)` metadata value:
  - `OutputPath`
  - `LoadPaths`
  - `OutputStyle`
  - `SourceMap`
  - `EmbedSources`
  - `QuietDeps`
  - `PkgImporter`
  - `SilenceDeprecations`
  - `FatalDeprecations`
  - `AdditionalArguments`
  - `Inputs`, if Scarlet.Sass exposes an explicit user-provided incremental input override.
  - `Outputs`, if Scarlet.Sass exposes an explicit user-provided incremental output override.
  - `StampFile`, if Scarlet.Sass exposes a user-provided stamp path.
- `build/` and `buildMultiTargeting/` imports contain equivalent runtime pack and task wiring where needed.

Integration tests:

- Compile real `.scss` to CSS.
- Compile `.sass` indented syntax.
- Compile directory-to-directory.
- Compile multiple entries in one task invocation.
- Source map generation and source embedding.
- Load paths with partials.
- `node_modules` load path scenario.
- `--pkg-importer=node` scenario if supported.
- Sass warnings and deprecation controls.
- Invalid Sass errors include useful file and line diagnostics.
- Cancellation and timeout behavior if timeout is supported.
- A project property changes compiler behavior, for example `SassOutputStyle=Compressed`.
- Item metadata overrides a global property, for example one item uses `OutputStyle=Expanded`.
- `SassLoadPaths` and item `LoadPaths` both reach the real compiler.
- Runtime download mode uses `SassRuntimeDownload`, `SassRuntimeDirectory`, and `SassVersionDownload`.
- Custom stamp/manifest path behavior, if exposed, is honored.

E2E tests:

- Package installation from local feed.
- Multi-TFM Razor Class Library, for example `net8.0;net9.0;net10.0`.
- Generated CSS appears in `wwwroot`.
- Generated CSS is packed under `staticwebassets/`.
- `dotnet pack --no-build` keeps previously generated static web assets and does not re-run Sass.
- Incremental build skips unchanged inputs.
- Changing a partial invalidates dependent output.
- Changing `SassOutputStyle`, source map settings, load paths, or runtime version regenerates output.
- Dart Sass `--update` internal skips still return generated CSS/map files to MSBuild as static web asset
  candidates.
- `dotnet clean` removes generated files.
- CSS isolation generated `.razor.css` is discovered by Razor, if CSS isolation ships.
- Runtime package selection works in outer build through `buildMultiTargeting`.
- The package does not rely on deprecated or legacy runtime properties.
- CLI forwards arguments exactly, including spaces, quotes, `--`, and non-ASCII.
- CLI `--scarlet-info` reports runtime source without downloading.

E2E tests should be similar in count and seriousness to Scarlet.Bun's suite:

- `package-installation`: restore from local feed, reference MSBuild and matching runtime package, build a
  fresh consumer project, verify output and runtime pack resolution.
- `multi-tfm`: fresh multi-targeted Razor Class Library, run Sass once in the outer build, verify generated
  static web assets are packed correctly.
- `incremental`: first build compiles, second build skips or lets Dart Sass `--update` skip, source/partial
  changes recompile, settings/runtime changes force regeneration.
- `monorepo-download`: shared download runtime directory, concurrent-safe download behavior, no duplicate
  downloads when multiple projects build.
- `cli-tool`: install `Scarlet.Sass.Cli` from local feed, verify argument forwarding and diagnostics.
- CSS isolation e2e, if shipped: generate `.razor.css`, verify Razor scoped CSS output and package behavior.

## Reuse From Scarlet.Bun

Reuse:

- Runtime pack item contract.
- Host platform detection, including musl detection.
- Runtime resolver and error message shape.
- Download mutex pattern.
- Chmod provider.
- CLI resolver and diagnostics pattern.
- Process launcher abstractions.
- Task dependency packaging tests.
- `build/` plus `buildMultiTargeting/` packaging.
- Static web assets timing lessons.
- E2E local feed tests.

Adapt:

- Incremental stamp logic should understand generated Sass outputs and maps.
- Output collection should preserve Sass diagnostics cleanly.
- Runtime package naming should follow Dart Sass release asset names, not Bun archive names.

Do not reuse directly:

- The generic `SassCommand` / `SassArguments` API as the primary MSBuild API.
- The legacy `SassRuntime_<rid>` property contract.
- JavaScript package install/build assumptions.
- Bun-specific environment variables or diagnostic text.

## Competitor Analysis

Scarlet.Sass makes sense only if it is deliberately positioned as a build tool, not as another runtime Sass
host. The competitors are useful proof that demand exists, but they optimize for different shapes.

### AspNetCore.SassCompiler

Strong points:

- It is easy to adopt in ASP.NET Core projects.
- It includes a build/runtime story that many users have already discovered.
- It has conventions for compiling Sass into web-facing CSS.
- It appears to cover CSS isolation scenarios, which are important for Razor and Blazor users.

Weak points:

- The `AspNetCore` name is narrower than the real use case. Sass compilation should work for Razor Class
  Libraries, Blazor libraries, and SDK-style projects that produce static web assets, not only ASP.NET Core
  apps.
- Runtime service registration is not the right default for a build package. `services.AddSassCompiler()`
  should be an optional runtime extension in a separate package, if it exists at all.
- Its JSON configuration is a package convention, not a Dart Sass concept. That makes it a separate config
  dialect rather than a natural part of MSBuild.
- If it starts a Dart Sass process per runtime compilation call, it pays process startup repeatedly in
  scenarios where a build tool can invoke Sass once in many-to-many mode.
- The public shape can blur runtime compilation and build-time asset generation, while Scarlet.Sass should
  stay focused on deterministic build output.

What Scarlet.Sass should borrow:

- CSS isolation scenarios.
- Developer-friendly conventions for default Sass folders.

What Scarlet.Sass should not borrow:

- The `AspNetCore` branding.
- JSON configuration.
- Runtime service registration in the core build package.
- A runtime-first mental model.

### EmbeddedSass.Net

Strong points:

- It uses the Embedded Sass Protocol, which is a more sophisticated host/compiler architecture than simply
  spawning the CLI for every operation.
- A persistent compiler process can be much faster for repeated runtime compilations.
- Its package split is cleaner: compiler, dependency injection, compiler binary, and MSBuild are separated.
- It exposes Sass concepts such as quiet dependencies and deprecation controls.
- It has a credible story for users who want a .NET Sass host library.

Weak points for Scarlet.Sass's target audience:

- The Embedded Sass Protocol is valuable, but it increases implementation and dependency complexity for an
  MSBuild task whose common case can be handled by one Dart Sass CLI process per outer build.
- If a package only ships `build/` and `buildTransitive/` assets, it risks missing the multi-targeting outer
  build path. Scarlet.Sass should include `buildMultiTargeting/` from day one.
- Published claims like "resolves static web assets" need e2e coverage against Blazor/Razor Class Libraries,
  multi-TFM projects, `dotnet pack`, and `dotnet pack --no-build`. Scarlet.Sass should make those tests a
  defining feature.
- Its broader compiler-host API is useful, but not the product Scarlet.Sass should lead with.

What Scarlet.Sass should borrow:

- Respect for native Sass concepts.
- Clear separation between build, CLI, runtime, and possible host-library concerns.
- Persistent compiler process as a future backend option.
- Manifest-driven generated file handling.
- Sass deprecation and quiet dependency controls.

What Scarlet.Sass should not borrow immediately:

- A mandatory Embedded Sass Protocol dependency in the v1 MSBuild package.
- A public compiler host surface as the core product.

### Why Scarlet.Sass Makes Sense

Scarlet.Sass has a strong reason to exist if it focuses on the part the competitors do not fully own:

- MSBuild-first configuration through properties and items.
- `SassBeforeStaticWebAssets`, mirroring the successful `BunBeforeStaticWebAssets` model.
- Correct outer-build behavior through `buildMultiTargeting/`.
- Multi-TFM Razor Class Library tests as a first-class release gate.
- Static web asset packaging tests, including `dotnet pack --no-build`.
- Pinned Dart Sass binaries through explicit NuGet runtime packages.
- A thin `dotnet sass` tool for repository-pinned CLI use.
- No ASP.NET Core runtime dependency for build-only users.
- No Scarlet-specific JSON configuration dialect.
- A backend abstraction that can adopt the Embedded Sass Protocol later if real build workloads prove it is
  worth the complexity.

The value proposition is not "we compile Sass and nobody else does." The value proposition is "we make Dart
Sass behave like a disciplined .NET build dependency, especially in Blazor and Razor Class Library projects
where static web assets, packing, and multi-targeting are easy to get subtly wrong."

## Documentation Requirements

The README should explain:

- Which package to install for build use.
- Which runtime package to install for the current build host.
- How to use download-on-demand mode.
- How to compile explicit Sass inputs to `wwwroot/css`.
- How to use `@(SassBeforeStaticWebAssets)`.
- How to configure output style and source maps.
- How `LoadPaths` work.
- How multi-TFM projects are handled.
- How Razor Class Library static web assets are handled.
- How CSS isolation works, if supported.
- Why there is no JSON config and why Scarlet.Sass will not invent one.
- Why Embedded Sass Protocol is not used in v1.
- How to run diagnostics.

## Open Questions

- Should CSS isolation ship in v1 or v1.1?
- Should source maps default to enabled in Debug, or should users opt in?
- Should `node_modules` be a default load path when the directory exists?
- Should `--pkg-importer=node` be enabled by default when `package.json` exists, or only explicitly?
- Should the CLI pointer package version exactly equal Dart Sass, or use a Scarlet revision suffix strategy?
- Should runtime packages include Android/RISC-V assets if Dart Sass publishes them, or only desktop/server
  build hosts?

## Recommended V1

1. Ship `Scarlet.Sass.MSBuild`, `Scarlet.Sass.Cli`, and platform runtime packages.
2. Use Dart Sass CLI, not Embedded Sass Protocol.
3. Compile with one process per task invocation in many-to-many mode.
4. Use MSBuild properties/items only.
5. Include `buildMultiTargeting`.
6. Support static web assets and RCL pack scenarios.
7. Include load paths, source maps, output style, quiet deps, and deprecation flags.
8. Add CSS isolation only if e2e tests prove it is reliable before release.
9. Keep an internal engine abstraction so Embedded Sass Protocol can be added later without breaking the
   public API.
