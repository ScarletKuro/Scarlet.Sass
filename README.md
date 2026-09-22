# Scarlet.Sass

![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/ScarletKuro/Scarlet.Sass/.github/workflows/ci.yml?branch=master&logo=github&style=flat-square)
[![codecov](https://codecov.io/gh/ScarletKuro/Scarlet.Sass/graph/badge.svg?token=A7MOQE06ZQ)](https://codecov.io/gh/ScarletKuro/Scarlet.Sass)
[![GitHub](https://img.shields.io/github/license/ScarletKuro/Scarlet.Sass?color=594ae2&logo=github&style=flat-square)](https://github.com/ScarletKuro/Scarlet.Sass/blob/master/LICENSE)

[Dart Sass](https://sass-lang.com/dart-sass/) for .NET, as a pinned NuGet dependency or .NET tool rather
than something you install separately.

Compile Sass/SCSS before Blazor, Razor Class Library, and ASP.NET Core static web assets are discovered,
or run the official Dart Sass CLI with `dotnet sass`, on Windows, Linux and macOS (x64 and ARM64).

## Which package do I want?

| | Package | Use it when |
|---|---|---|
| **During a build** | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Sass.MSBuild?color=ff4081&label=Scarlet.Sass.MSBuild&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Sass.MSBuild/) | You want `dotnet build` to compile Sass into static web assets - Blazor, Razor Class Libraries, ASP.NET Core |
| **On the command line** | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Sass.Cli?color=ff4081&label=Scarlet.Sass.Cli&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Sass.Cli/) | You want `dotnet sass ...`, pinned per repository |

They are independent - use either, or both.

### Scarlet.Sass.MSBuild - Sass during `dotnet build`

```bash
dotnet add package Scarlet.Sass.MSBuild
dotnet add package Scarlet.Sass.Runtime.windows-x64
```

```xml
<ItemGroup>
  <SassBeforeStaticWebAssets Include="Sass">
    <OutputPath>wwwroot/css</OutputPath>
    <OutputStyle>Compressed</OutputStyle>
  </SassBeforeStaticWebAssets>
</ItemGroup>
```

The Dart Sass runtime comes either from a platform-specific `Scarlet.Sass.Runtime.*` package or from an
on-demand download, whichever suits your build.

Full documentation: [Scarlet.Sass.MSBuild](src/Scarlet.Sass.MSBuild/README.md) - installation, runtime
options, task properties, item metadata, multi-targeting, static web assets, incrementality, and the
Embedded Sass Protocol decision.

### Scarlet.Sass.Cli - Sass on the command line

```bash
dotnet new tool-manifest
dotnet tool install Scarlet.Sass.Cli
dotnet sass Sass:wwwroot/css --style=compressed
```

The tool version is the Dart Sass version, so `.config/dotnet-tools.json` pins Sass alongside the rest of
your tooling. `Scarlet.Sass.Cli` is a pointer package; installing it also pulls a matching
`Scarlet.Sass.Cli.*` sub-package for your platform, and that one embeds Dart Sass, so it needs no network
at run time.

Full documentation: [Scarlet.Sass.Cli](src/Scarlet.Sass.Cli/README.md) - installing, argument forwarding,
runtime resolution, diagnostics, and environment variables.

## Available Packages

| Package | Contains |
|---------|----------|
| [Scarlet.Sass.MSBuild](https://www.nuget.org/packages/Scarlet.Sass.MSBuild/) | The MSBuild task. Versioned independently. |
| [Scarlet.Sass.Cli](https://www.nuget.org/packages/Scarlet.Sass.Cli/) | The `dotnet sass` tool. Version = the embedded Dart Sass version. |
| [Scarlet.Sass.Runtime.windows-x64](https://www.nuget.org/packages/Scarlet.Sass.Runtime.windows-x64/) | Dart Sass for Windows x64 |
| [Scarlet.Sass.Runtime.windows-arm64](https://www.nuget.org/packages/Scarlet.Sass.Runtime.windows-arm64/) | Dart Sass for Windows ARM64 |
| [Scarlet.Sass.Runtime.linux-x64](https://www.nuget.org/packages/Scarlet.Sass.Runtime.linux-x64/) | Dart Sass for Linux x64 |
| [Scarlet.Sass.Runtime.linux-arm64](https://www.nuget.org/packages/Scarlet.Sass.Runtime.linux-arm64/) | Dart Sass for Linux ARM64 |
| [Scarlet.Sass.Runtime.linux-x64-musl](https://www.nuget.org/packages/Scarlet.Sass.Runtime.linux-x64-musl/) | Dart Sass for Linux x64, musl (Alpine) |
| [Scarlet.Sass.Runtime.linux-arm64-musl](https://www.nuget.org/packages/Scarlet.Sass.Runtime.linux-arm64-musl/) | Dart Sass for Linux ARM64, musl (Alpine) |
| [Scarlet.Sass.Runtime.darwin-x64](https://www.nuget.org/packages/Scarlet.Sass.Runtime.darwin-x64/) | Dart Sass for macOS x64 |
| [Scarlet.Sass.Runtime.darwin-arm64](https://www.nuget.org/packages/Scarlet.Sass.Runtime.darwin-arm64/) | Dart Sass for macOS ARM64 |

The `Scarlet.Sass.Runtime.*` packages are consumed by `Scarlet.Sass.MSBuild` and can also be installed
directly (see its README); the CLI embeds its own Dart Sass and does not use them. Their package version
is the Dart Sass version they contain.

`Scarlet.Sass.Cli` restores its own per-platform `Scarlet.Sass.Cli.*` sub-packages (one per RID, plus a
portable `.any` fallback) automatically - unlike the `Runtime.*` packages, these are a `dotnet tool`
implementation detail, never meant to be installed directly, so they aren't listed here.

## Supported Platforms

Windows, Linux and macOS on **x64 or arm64**, including musl-based Linux distributions such as Alpine. Any
other architecture gets an explanatory error rather than a mismatched binary - point at your own Sass with
`SCARLET_SASS_PATH` (CLI) or `SassRuntimeDirectory` (MSBuild) if you need one of them.

## Configuration

Scarlet.Sass intentionally does not define `sasscompiler.json`, `appsettings.json`, or any other
Scarlet-specific JSON configuration convention. Use Dart Sass CLI arguments, MSBuild properties, or
`SassBeforeStaticWebAssets` item metadata instead.

## Development

### Building the Package

```bash
dotnet build Scarlet.Sass.MSBuild.slnx
```

### Running Tests

Unit tests:
```bash
dotnet test tests/Scarlet.Sass.MSBuild.Tests/Scarlet.Sass.MSBuild.Tests.csproj
```

Integration tests:
```bash
dotnet test tests/Scarlet.Sass.MSBuild.IntegrationTests/Scarlet.Sass.MSBuild.IntegrationTests.csproj
```

CLI tests:
```bash
dotnet test tests/Scarlet.Sass.Cli.Tests/Scarlet.Sass.Cli.Tests.csproj
```

All tests:
```bash
dotnet test Scarlet.Sass.MSBuild.slnx
```

End-to-end scenarios pack real packages into a local feed and consume them from a temporary project. They
take a workspace path, a package version and a Dart Sass version:
```bash
tests/e2e/package-installation/verify.sh "$PWD" 1.0.0-local 1.104.1
tests/e2e/cli-tool/verify.sh            "$PWD" 1.104.1-local 1.104.1
```

### Creating a Package

```bash
dotnet pack src/Scarlet.Sass.MSBuild/Scarlet.Sass.MSBuild.csproj
```

The CLI packs into ten packages at once - one per runtime identifier, a portable fallback and a
top-level pointer package:
```bash
dotnet pack src/Scarlet.Sass.Cli/Scarlet.Sass.Cli.csproj
```

`src/Scarlet.Sass.Core` is a shared library used by both shipping packages. It is deliberately not
published: the MSBuild package packs the assembly into its `tools/` folder, and the CLI carries it in its
publish output.

## Requirements

- .NET / .NET Core (no .NET Framework support)
- Supported on Windows, Linux, and macOS

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

### Bundled Software Licenses

This package distributes Dart Sass binaries, which include:

- **Dart Sass**: MIT License - Copyright (c) 2016, Google Inc.
- **Dart SDK runtime files**: BSD-style license from the Dart project, as distributed inside the official
  Dart Sass release archive

See [LICENSE-3RD-PARTY.txt](LICENSE-3RD-PARTY.txt) for bundled third-party license notices.

## Credits

- Built by [ScarletKuro](https://github.com/ScarletKuro)
- Uses [Dart Sass](https://sass-lang.com/dart-sass/) - the primary implementation of Sass

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for how to build, test, and submit a
Pull Request.
