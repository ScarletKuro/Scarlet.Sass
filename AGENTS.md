# AGENTS.md

This document helps AI agents and contributors work on Scarlet.Sass without confusing it with Scarlet.Bun.

## Project Overview

Scarlet.Sass integrates the official Dart Sass compiler into .NET builds. The MSBuild package compiles explicit `@(SassBeforeStaticWebAssets)` items before ASP.NET Core, Blazor, and Razor Class Library static web assets are discovered. The CLI package exposes `dotnet sass ...` and forwards Dart Sass arguments verbatim.

## Dart Sass

Before changing Sass argument rendering, release archive mapping, load paths, `--pkg-importer`, source maps, deprecation flags, or runtime download behavior, verify the behavior against official Dart Sass documentation, the pinned Dart Sass binary, or Dart Sass GitHub releases.

Important Sass-specific facts:
- The runtime packages contain the official `dart-sass` folder.
- Official bundles should be launched as `dart-sass/src/dart(.exe) dart-sass/src/sass.snapshot`, not through `sass` or `sass.bat`, when that layout is present. The public launcher remains the display/probe path. This avoids wrapper-child timeout and orphan behavior, especially on Windows.
- Unix launchers need both `dart-sass/sass` and `dart-sass/src/dart` to be executable.
- The CLI is `sass`; the .NET tool command is `dotnet sass`.
- Scarlet CLI environment variables use the `SCARLET_SASS_*` prefix.
- There is no Scarlet JSON configuration convention.

## MSBuild

Before changing tasks, targets, props, package layout, static web asset wiring, or clean behavior, read `.github/agents/msbuild-llms-full.txt`.

Critical MSBuild concepts for this repo:
- Task dependencies must be packed next to `Scarlet.Sass.MSBuild.dll` under `tools/netstandard2.0/`.
- Static web asset output must be injected into `@(Content)` after compilation.
- Generated files must be tracked for `dotnet clean`.
- Multi-TFM projects need the outer build path through `buildMultiTargeting/`.
- Build-host RID selection is handled by `@(SassRuntimePack)`, not NuGet target RID resolution.

## Project Structure

```text
/
├── build/
│   └── SassRuntime.targets
├── src/
│   ├── Scarlet.Sass.Core/
│   ├── Scarlet.Sass.Cli/
│   ├── Scarlet.Sass.MSBuild/
│   └── Scarlet.Sass.Runtime.{rid}/
├── samples/
├── tests/
│   ├── Scarlet.Sass.Cli.Tests/
│   ├── Scarlet.Sass.MSBuild.Tests/
│   ├── Scarlet.Sass.MSBuild.IntegrationTests/
│   └── e2e/
├── tools/
│   ├── download-sass.sh
│   └── download-sass.ps1
├── README.md
└── SCARLET_SASS_SPEC.md
```

## Key Components

`Scarlet.Sass.Core` contains platform detection, runtime pack resolution, Dart Sass download support, chmod helpers, and shared process/file abstractions.

`Scarlet.Sass.MSBuild` contains `SassCompileTask`, build assets, and package metadata. It targets `netstandard2.0` so MSBuild can load it broadly.

`Scarlet.Sass.Cli` is the `dotnet sass` tool. It keeps `--scarlet-info` as the only reserved first-position flag; everything else belongs to Dart Sass.

`Scarlet.Sass.Runtime.*` packages download and pack the official Dart Sass release archives into `runtimes/<rid>/native/dart-sass/`.

## Runtime Discovery Contract

Dart Sass runs on the build host. Runtime packages contribute `@(SassRuntimePack)` items from both `build/` and `buildMultiTargeting/`, and the resolver selects the pack matching the host RID. Do not add a legacy `SassRuntime_<rid>` property contract unless there is an actual published compatibility burden.

## Build And Test

```bash
dotnet build --configuration Release
dotnet test --configuration Release
dotnet pack src/Scarlet.Sass.MSBuild/Scarlet.Sass.MSBuild.csproj --configuration Release
```

Useful targeted tests:

```bash
dotnet test tests/Scarlet.Sass.MSBuild.Tests/Scarlet.Sass.MSBuild.Tests.csproj
dotnet test tests/Scarlet.Sass.MSBuild.IntegrationTests/Scarlet.Sass.MSBuild.IntegrationTests.csproj
dotnet test tests/Scarlet.Sass.Cli.Tests/Scarlet.Sass.Cli.Tests.csproj
```

The e2e scripts pack local packages into a local feed and consume them from fresh projects. They are the best guard for package layout, static web assets, clean, pack, multi-TFM, runtime pack selection, and CLI behavior.

## Common Failure Modes

- Task load failures usually mean a dependency is missing from `tools/netstandard2.0/`.
- Runtime resolution failures usually mean the expected `@(SassRuntimePack)` item did not import, the host RID is unsupported, or the runtime package was not referenced.
- Unix `EACCES` failures usually mean either `dart-sass/sass` or `dart-sass/src/dart` was not chmod'd after NuGet extraction or download.
- Clean failures usually mean the generated manifest or `SassClean` target stopped lining up with the stamp directory.
- CLI stdout must stay reserved for Dart Sass output; Scarlet diagnostics go to stderr.

## Naming Hygiene

Be careful when porting Scarlet.Bun code. Do not blindly replace `bun` with `Sass`:

- `ubuntu` must stay `ubuntu`.
- `bundle`, `bundled`, and `bundler` must stay normal English words unless the text really means Dart Sass.
- `dotnet sass` and `SCARLET_SASS_*` are lowercase/uppercase contracts.
- File names such as `download-sass.sh` stay lowercase.
- References to Scarlet.Bun in `SCARLET_SASS_SPEC.md` are often intentional comparison points.
