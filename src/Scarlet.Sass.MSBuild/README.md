# Scarlet.Sass.MSBuild

Compile Dart Sass before ASP.NET Core, Blazor, and Razor Class Library static web assets are discovered.

Scarlet.Sass.MSBuild is intentionally explicit: it compiles only `@(SassBeforeStaticWebAssets)` items. There is no automatic `Sass/` convention and no `sasscompiler.json` or `appsettings.json` configuration dialect.

## Table of Contents

- [Install](#install)
  - [Option 1: Runtime Download](#option-1-runtime-download)
  - [Option 2: Conditional Package References](#option-2-conditional-package-references)
  - [How the Runtime Is Discovered](#how-the-runtime-is-discovered)
- [Compile Before Static Web Assets](#compile-before-static-web-assets)
- [Properties](#properties)
- [Item Metadata](#item-metadata)
- [Task Parameters](#task-parameters)
  - [Output Parameters](#output-parameters)
- [Incrementality](#incrementality)
- [dotnet watch Integration](#dotnet-watch-integration)
- [Embedded Sass Protocol](#embedded-sass-protocol)
- [CSS Isolation](#css-isolation)
- [Supported Platforms](#supported-platforms)
- [Links](#links)
- [License](#license)

## Install

```bash
dotnet add package Scarlet.Sass.MSBuild
dotnet add package Scarlet.Sass.Runtime.windows-x64
```

Use the runtime package that matches the **build host**, not the project's target RID. Package names follow Dart Sass's own platform naming, which does not match .NET RIDs:

| Build host RID | Runtime package |
| --- | --- |
| `win-x64` | `Scarlet.Sass.Runtime.windows-x64` |
| `win-arm64` | `Scarlet.Sass.Runtime.windows-arm64` |
| `linux-x64` | `Scarlet.Sass.Runtime.linux-x64` |
| `linux-arm64` | `Scarlet.Sass.Runtime.linux-arm64` |
| `linux-musl-x64` | `Scarlet.Sass.Runtime.linux-x64-musl` |
| `linux-musl-arm64` | `Scarlet.Sass.Runtime.linux-arm64-musl` |
| `osx-x64` | `Scarlet.Sass.Runtime.darwin-x64` |
| `osx-arm64` | `Scarlet.Sass.Runtime.darwin-arm64` |

### Option 1: Runtime Download

No runtime package at all — the build fetches Dart Sass on first use and caches it:

```xml
<PropertyGroup>
  <SassRuntimeDownload>true</SassRuntimeDownload>
  <SassVersionDownload>1.104.1</SassVersionDownload>
  <SassRuntimeDirectory>$(MSBuildProjectDirectory)\.sass</SassRuntimeDirectory>
</PropertyGroup>
```

`SassRuntimeDirectory` is required here — it is where the download lands. Point several projects at one shared directory and they will coordinate: the download is guarded by a global mutex, and a runtime is only treated as usable once its version marker is written, so a build never picks up a half-extracted copy.

### Option 2: Conditional Package References

Each runtime package carries a full Dart Sass, so referencing all eight is wasteful. Detect the host and reference only the package it needs — this is what a repository building on more than one platform wants in `Directory.Build.props`:

```xml
<!-- Detect the build host -->
<PropertyGroup>
  <IsWindows Condition="'$(OS)' == 'Windows_NT'">true</IsWindows>
  <IsLinux Condition="Exists('/proc')">true</IsLinux>
  <IsMacOS Condition="Exists('/System/Library/CoreServices/SystemVersion.plist')">true</IsMacOS>
  <IsMusl Condition="Exists('/lib/ld-musl-x86_64.so.1') OR Exists('/lib/ld-musl-aarch64.so.1')">true</IsMusl>
  <IsX64 Condition="'$(PROCESSOR_ARCHITECTURE)' == 'AMD64' OR '$(PROCESSOR_IDENTIFIER)' == 'AMD64'">true</IsX64>
  <IsARM64 Condition="'$(PROCESSOR_ARCHITECTURE)' == 'ARM64' OR '$(PROCESSOR_IDENTIFIER)' == 'ARM64'">true</IsARM64>
</PropertyGroup>

<ItemGroup Condition="'$(IsWindows)' == 'true' AND '$(IsX64)' == 'true'">
  <PackageReference Include="Scarlet.Sass.Runtime.windows-x64" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsWindows)' == 'true' AND '$(IsARM64)' == 'true'">
  <PackageReference Include="Scarlet.Sass.Runtime.windows-arm64" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsX64)' == 'true' AND '$(IsMusl)' != 'true'">
  <PackageReference Include="Scarlet.Sass.Runtime.linux-x64" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsARM64)' == 'true' AND '$(IsMusl)' != 'true'">
  <PackageReference Include="Scarlet.Sass.Runtime.linux-arm64" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsX64)' == 'true' AND '$(IsMusl)' == 'true'">
  <PackageReference Include="Scarlet.Sass.Runtime.linux-x64-musl" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsARM64)' == 'true' AND '$(IsMusl)' == 'true'">
  <PackageReference Include="Scarlet.Sass.Runtime.linux-arm64-musl" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsMacOS)' == 'true' AND '$(IsX64)' == 'true'">
  <PackageReference Include="Scarlet.Sass.Runtime.darwin-x64" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
<ItemGroup Condition="'$(IsMacOS)' == 'true' AND '$(IsARM64)' == 'true'">
  <PackageReference Include="Scarlet.Sass.Runtime.darwin-arm64" Version="1.104.1" PrivateAssets="all" />
</ItemGroup>
```

`IsMusl` is what separates Alpine from glibc distributions; without it a musl host would restore the glibc package and fail at launch rather than at restore.

### How the Runtime Is Discovered

Dart Sass runs on the machine doing the build, not the machine the project targets, so NuGet's RID resolution is the wrong mechanism — it resolves against `$(RuntimeIdentifier)`. Instead each `Scarlet.Sass.Runtime.*` package contributes a `SassRuntimePack` item from its `build/*.props`, and the task picks the one matching the build host.

You normally never write one. You would if you want the build to use a Dart Sass you supply yourself — a pre-release, or a locally built one — without waiting for a runtime package:

```xml
<ItemGroup>
  <SassRuntimePack Include="MyCompany.Sass.linux-x64-custom">
    <Rid>linux-x64</Rid>
    <RuntimesPath>$(MSBuildProjectDirectory)/sass/runtimes</RuntimesPath>
    <Priority>100</Priority>
  </SassRuntimePack>
</ItemGroup>
```

| Metadata | Required | Description |
| --- | --- | --- |
| `Rid` | Yes | The runtime identifier this pack serves, for example `osx-arm64` |
| `RuntimesPath` | Yes | Directory containing `<rid>/native/dart-sass/sass` (`sass.bat` on Windows) |
| `Variant` | No | Build variant, shown in build logs and error messages |
| `Priority` | No | Higher wins when several packs serve the same `Rid`. Defaults to `0`; ties break by pack id, so the result never depends on restore order |

Precedence: an explicit `SassRuntimeDirectory` wins over every pack, and `SassRuntimeDownload=true` bypasses pack resolution entirely. If a pack's launcher is missing, the next candidate for the same RID is tried.

A pack that omits `Rid` or `RuntimesPath` is skipped with a build warning naming it, and a non-numeric `Priority` warns and falls back to `0`. A typo in a hand-authored pack would otherwise silently resolve to a different one.

> **Note:** Runtime packages are development dependencies, so their props apply to the project referencing them directly. They do not flow to projects that reference *that* project — add the `PackageReference` in each project that compiles Sass, or in a shared `Directory.Build.props`.

## Compile Before Static Web Assets

Directory-to-directory:

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass">
    <OutputPath>wwwroot/css</OutputPath>
    <OutputStyle>Compressed</OutputStyle>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

File-to-file:

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass/site.scss">
    <OutputPath>wwwroot/css/site.css</OutputPath>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

Sass partials whose file name starts with `_` are not entry points. They can still be imported or used by other Sass files.

## Properties

| Property | Default | Meaning |
| --- | --- | --- |
| `SassEnabled` | `true` | Enables `RunSassBeforeStaticWebAssets`. |
| `SassOutputStyle` | `Auto` | `Auto`, `Expanded`, or `Compressed`. `Auto` is expanded for Debug and compressed otherwise. |
| `SassSourceMap` | `Auto` | `Auto`, `true`, or `false`. |
| `SassEmbedSources` | `Auto` | `Auto`, `true`, or `false`. |
| `SassQuietDeps` | `false` | Adds `--quiet-deps`. |
| `SassLoadPaths` | empty | Semicolon-separated load paths passed as `--load-path`. |
| `SassPkgImporter` | empty | Set to `node` to pass `--pkg-importer=node`. |
| `SassSilenceDeprecations` | empty | Semicolon-separated values for `--silence-deprecation`. |
| `SassFatalDeprecations` | empty | Semicolon-separated values for `--fatal-deprecation`. |
| `SassAdditionalArguments` | empty | Extra raw Dart Sass arguments. |
| `SassRuntimeDirectory` | empty | Explicit runtime directory containing `<rid>/native/dart-sass/<sass launcher>`. |
| `SassRuntimeDownload` | `false` | Download the runtime instead of using runtime packages. |
| `SassVersionDownload` | empty | Version to download. Empty resolves the latest GitHub release. |
| `SassDownloadMutexTimeoutSeconds` | `300` | Timeout for concurrent download coordination. |
| `SassStampDirectory` | `$(IntermediateOutputPath)\Scarlet.Sass` | Settings stamp and generated-file manifest directory. |
| `SassTimeoutMilliseconds` | `0` | Maximum time to wait for each Dart Sass invocation before killing it. `0` waits indefinitely. |

## Item Metadata

`SassBeforeStaticWebAssets` supports the same compile metadata as the global properties:

```xml
<SassBeforeStaticWebAssets Include="Sass">
  <OutputPath>wwwroot/css</OutputPath>
  <OutputStyle>Compressed</OutputStyle>
  <SourceMap>true</SourceMap>
  <EmbedSources>false</EmbedSources>
  <QuietDeps>true</QuietDeps>
  <LoadPaths>node_modules;shared/styles</LoadPaths>
  <PkgImporter>node</PkgImporter>
  <SilenceDeprecations>import;global-builtin</SilenceDeprecations>
  <FatalDeprecations>color-functions</FatalDeprecations>
  <AdditionalArguments>--charset</AdditionalArguments>
</SassBeforeStaticWebAssets>
```

`OutputPath` is required. If the item points at a directory, `OutputPath` must be a directory. If the item points at a file, `OutputPath` can be a `.css` file or a path whose extension will be changed to `.css`.

`AdditionalArguments` is applied last. If it contains repeated `--source-map` or `--no-source-map` flags,
the last one controls both Dart Sass and Scarlet's generated-file manifest.

## Task Parameters

`RunSassBeforeStaticWebAssets` sets these from the properties above. Call `SassCompileTask` directly only if you need a compile outside that target.

| Parameter | Required | Description | Default |
| --- | --- | --- | --- |
| `Compilations` | Yes | The items to compile, normally `@(SassBeforeStaticWebAssets)` | - |
| `ProjectDirectory` | Yes | Directory that relative input, output and stamp paths resolve against, normally `$(MSBuildProjectDirectory)` | - |
| `Configuration` | No | Drives the `Auto` defaults for `OutputStyle`, `SourceMap` and `EmbedSources` | `Debug` |
| `OutputStyle` | No | `Auto`, `Expanded` or `Compressed` | `Auto` |
| `SourceMap` | No | `Auto`, `true` or `false` | `Auto` |
| `EmbedSources` | No | `Auto`, `true` or `false` | `Auto` |
| `QuietDeps` | No | Adds `--quiet-deps` | `false` |
| `LoadPaths` | No | Semicolon-separated `--load-path` values | empty |
| `PkgImporter` | No | Passed as `--pkg-importer` | empty |
| `SilenceDeprecations` | No | Semicolon-separated `--silence-deprecation` values | empty |
| `FatalDeprecations` | No | Semicolon-separated `--fatal-deprecation` values | empty |
| `AdditionalArguments` | No | Extra raw Dart Sass arguments | empty |
| `StampDirectory` | No | Directory for the settings stamp and generated-file manifest | `obj/Scarlet.Sass` |
| `RuntimeDirectory` | No | Explicit runtime directory. Overrides `RuntimePacks`. Required when `SassRuntimeDownload` is true | null |
| `RuntimePacks` | No | The runtimes available to the build, normally `@(SassRuntimePack)`. See [How the Runtime Is Discovered](#how-the-runtime-is-discovered) | empty |
| `SassRuntimeDownload` | No | Download Dart Sass instead of using runtime packs | `false` |
| `SassVersionDownload` | No | Version to download. Empty resolves the latest GitHub release | empty |
| `DownloadMutexTimeoutSeconds` | No | Seconds to wait when another process holds the download mutex | `300` |
| `TimeoutMilliseconds` | No | Maximum time per Dart Sass invocation before it is killed. `0` waits indefinitely | `0` |

### Output Parameters

| Parameter | Description |
| --- | --- |
| `GeneratedFiles` | The CSS and source map files this run produced. Each carries `RelativePath` metadata, used to add them to `@(Content)` and `@(FileWrites)` |
| `RemovedFiles` | Previously generated files that are no longer produced, removed from `@(Content)` and `@(None)` so a renamed stylesheet does not leave a stale asset behind |

## Incrementality

Scarlet.Sass invokes Dart Sass with `--update`, so Dart Sass owns Sass dependency freshness. Scarlet owns the MSBuild side:

- settings/runtime stamps
- generated-file manifests
- stale output deletion
- `dotnet clean` cleanup
- static web asset `@(Content)` and `@(FileWrites)` injection

If settings or runtime selection changes, expected CSS and map outputs are deleted before Dart Sass runs so `--update` cannot incorrectly skip them.

## dotnet watch Integration

`dotnet watch` only reloads on changes to files it already knows about (`.cs`, `.razor`, `.cshtml`, and a few others) — it has no idea your Sass sources exist, so editing a `.scss` file does nothing until you rebuild manually. Tell it about them with a `Watch` item and it will trigger a normal build, Sass compile included, whenever they change:

```xml
<ItemGroup>
  <Watch Include="assets\styles\**\*.scss;assets\styles\**\*.sass" />
</ItemGroup>
```

Point the globs at your Sass sources, not `wwwroot`. The compile writes CSS and source maps into `wwwroot`, so watching the output would make every rebuild trigger another rebuild.

Include partials. Editing `_variables.scss` changes the CSS of every stylesheet that uses it, and `--update` will recompile those dependents — but only once something triggers a build in the first place, and a partial is never itself an entry point.

`Watch` knows nothing about Sass; it is only a trip-wire telling `dotnet watch` to treat a change to these files like a change to a `.cs` file. When it fires, `dotnet watch` runs an ordinary build, and your `SassBeforeStaticWebAssets` items already compile on every build — so the `Watch` item never invokes Dart Sass itself, it just makes the build that was always going to invoke it happen more often.

Run `dotnet watch build`, or `dotnet watch run` for a Blazor or ASP.NET Core app, which also refreshes the browser for the regenerated static assets.

> **Do not put `--watch` in `SassAdditionalArguments`.** Dart Sass's watch mode never exits, so the build would hang rather than finish. With the default `SassTimeoutMilliseconds` of `0` it waits indefinitely; with a timeout set, the build fails instead. If per-save latency becomes the bottleneck, run `dotnet sass --watch` as a separate long-lived process and manage its lifecycle yourself.

This rides entirely on `dotnet watch`'s existing file watching — no code in this package — so it costs a full MSBuild build per save rather than an instant incremental rebuild.

## Embedded Sass Protocol

The Embedded Sass Protocol is valuable for a long-lived compiler host that reuses a process across many compilations. Scarlet.Sass.MSBuild v1 uses the Dart Sass CLI instead. The package is scoped around build integration and static web assets, and the CLI keeps the implementation simpler while preserving a path to add another engine later if benchmarks justify it.

## CSS Isolation

CSS isolation integration is intentionally deferred. v1 focuses on project/RCL static web asset generation through explicit `SassBeforeStaticWebAssets` items.

## Supported Platforms

Windows, Linux and macOS on **x64 or arm64**, including musl-based distributions such as Alpine. Any other architecture gets an explanatory error rather than a mismatched binary — point `SassRuntimeDirectory` at your own Dart Sass if you need one.

Requires the .NET SDK. The task targets `netstandard2.0`, so it loads in both `dotnet build` and Visual Studio's MSBuild.

## Links

- [Source, samples and full documentation](https://github.com/ScarletKuro/Scarlet.Sass)
- [Scarlet.Sass.Cli](https://www.nuget.org/packages/Scarlet.Sass.Cli/) — Dart Sass on the command line
- [Dart Sass documentation](https://sass-lang.com/documentation/)

## License

Scarlet.Sass is MIT licensed. Dart Sass is distributed under its own MIT license; see `LICENSE-3RD-PARTY.txt` in the package.
