# Scarlet.Sass.MSBuild

Compile Dart Sass before ASP.NET Core, Blazor, and Razor Class Library static web assets are discovered.

Scarlet.Sass.MSBuild is intentionally explicit: it compiles only `@(SassBeforeStaticWebAssets)` items. There is no automatic `Sass/` convention and no `sasscompiler.json` or `appsettings.json` configuration dialect.

## Table of Contents

- [Install](#install)
- [Compile Before Static Web Assets](#compile-before-static-web-assets)
- [Properties](#properties)
- [Item Metadata](#item-metadata)
- [Incrementality](#incrementality)
- [dotnet watch Integration](#dotnet-watch-integration)
- [Embedded Sass Protocol](#embedded-sass-protocol)
- [CSS Isolation](#css-isolation)
- [License](#license)

## Install

```bash
dotnet add package Scarlet.Sass.MSBuild
dotnet add package Scarlet.Sass.Runtime.windows-x64
```

Use the runtime package that matches the build host:

- `Scarlet.Sass.Runtime.windows-x64`
- `Scarlet.Sass.Runtime.windows-arm64`
- `Scarlet.Sass.Runtime.linux-x64`
- `Scarlet.Sass.Runtime.linux-arm64`
- `Scarlet.Sass.Runtime.linux-x64-musl`
- `Scarlet.Sass.Runtime.linux-arm64-musl`
- `Scarlet.Sass.Runtime.darwin-x64`
- `Scarlet.Sass.Runtime.darwin-arm64`

Or let the build download Dart Sass:

```xml
<PropertyGroup>
  <SassRuntimeDownload>true</SassRuntimeDownload>
  <SassVersionDownload>1.104.1</SassVersionDownload>
  <SassRuntimeDirectory>$(MSBuildProjectDirectory)\.sass</SassRuntimeDirectory>
</PropertyGroup>
```

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
| `SassVersionDownload` | empty | Version to download. Empty means the pinned package version. |
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

## License

Scarlet.Sass is MIT licensed. Dart Sass is distributed under its own MIT license; see `LICENSE-3RD-PARTY.txt` in the package.
