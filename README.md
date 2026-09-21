# Scarlet.Sass

![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/ScarletKuro/Scarlet.Sass/.github/workflows/ci.yml?branch=master&logo=github&style=flat-square)
[![codecov](https://codecov.io/gh/ScarletKuro/Scarlet.Sass/graph/badge.svg?token=A7MOQE06ZQ)](https://codecov.io/gh/ScarletKuro/Scarlet.Sass)
[![GitHub](https://img.shields.io/github/license/ScarletKuro/Scarlet.Sass?color=594ae2&logo=github&style=flat-square)](https://github.com/ScarletKuro/Scarlet.Sass/blob/master/LICENSE)

Dart Sass for .NET builds and tools, packaged in the same Scarlet style as Scarlet.Bun.

Scarlet.Sass has two public entry points:

| Package | Purpose |
| --- | --- |
| `Scarlet.Sass.MSBuild` | Compile explicit Sass/SCSS inputs during `dotnet build`, before Blazor/Razor static web assets are discovered. |
| `Scarlet.Sass.Cli` | Run Dart Sass as a .NET tool with `dotnet sass ...`, forwarding Sass arguments verbatim. |

The runtime is the official Dart Sass CLI. Scarlet.Sass does not use the Embedded Sass Protocol in v1: MSBuild builds are short lived, Dart Sass already owns Sass dependency freshness through `--update`, and the protocol would add host/compiler complexity before we have measurements showing it helps this package shape.

## MSBuild

Install the task package and provide a runtime, either through a runtime package or download mode:

```bash
dotnet add package Scarlet.Sass.MSBuild
dotnet add package Scarlet.Sass.Runtime.windows-x64
```

Then declare exactly what should compile:

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass">
    <OutputPath>wwwroot/css</OutputPath>
    <OutputStyle>Compressed</OutputStyle>
    <LoadPaths>node_modules</LoadPaths>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

There is no automatic `Sass/` folder convention. `node_modules`, load paths, and `--pkg-importer=node` are explicit opt-ins because they are Dart Sass concepts, not Scarlet conventions.

Supported runtime packages:

- `Scarlet.Sass.Runtime.windows-x64`
- `Scarlet.Sass.Runtime.windows-arm64`
- `Scarlet.Sass.Runtime.linux-x64`
- `Scarlet.Sass.Runtime.linux-arm64`
- `Scarlet.Sass.Runtime.linux-x64-musl`
- `Scarlet.Sass.Runtime.linux-arm64-musl`
- `Scarlet.Sass.Runtime.darwin-x64`
- `Scarlet.Sass.Runtime.darwin-arm64`

Download mode:

```xml
<PropertyGroup>
  <SassRuntimeDownload>true</SassRuntimeDownload>
  <SassVersionDownload>1.104.1</SassVersionDownload>
  <SassRuntimeDirectory>$(MSBuildProjectDirectory)\.sass</SassRuntimeDirectory>
</PropertyGroup>
```

See [Scarlet.Sass.MSBuild](src/Scarlet.Sass.MSBuild/README.md) for the full property and metadata list.

## CLI

```bash
dotnet new tool-manifest
dotnet tool install Scarlet.Sass.Cli
dotnet sass --version
dotnet sass Sass:wwwroot/css --style=compressed
```

`dotnet sass ...` forwards every argument to Dart Sass except first-position `--scarlet-info`:

```bash
dotnet sass --scarlet-info
dotnet sass --scarlet-info --json
```

CLI environment variables use the `SCARLET_SASS_*` prefix, for example `SCARLET_SASS_PATH`, `SCARLET_SASS_VERSION`, and `SCARLET_SASS_CACHE_DIR`.

See [Scarlet.Sass.Cli](src/Scarlet.Sass.Cli/README.md) for details.

## Configuration

Scarlet.Sass intentionally does not define `sasscompiler.json`, `appsettings.json`, or any other Scarlet-specific JSON configuration convention. v1 exposes Dart Sass concepts as MSBuild properties/item metadata and CLI arguments only.

## Development

```bash
dotnet build Scarlet.Sass.MSBuild.slnx
dotnet test Scarlet.Sass.MSBuild.slnx
```

## License

Scarlet.Sass is MIT licensed. Dart Sass is distributed under its own MIT license; see [LICENSE-3RD-PARTY.txt](LICENSE-3RD-PARTY.txt).
