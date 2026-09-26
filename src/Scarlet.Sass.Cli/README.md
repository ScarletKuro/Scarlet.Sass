# Scarlet.Sass.Cli

Run [Dart Sass](https://sass-lang.com/dart-sass/) — the reference implementation of Sass — as a .NET
tool.

```bash
dotnet sass --version
dotnet sass Sass:wwwroot/css --style=compressed
```

Every argument is forwarded to Dart Sass verbatim, so anything valid after `sass` is valid after
`dotnet sass`.

## Why not just install Dart Sass?

You still can — nothing here stops you, and for a solo project with no CI this buys you little. It earns
its keep once a repository has more than one contributor or a CI pipeline, because it makes Dart Sass
pinned and installed the same way as the rest of your .NET tooling, instead of a separate step with its
own installer:

- **Pinned like everything else.** The Dart Sass version lives in `.config/dotnet-tools.json` next to
  `dotnet-ef`, `dotnet-format`, and friends. `dotnet tool restore` brings it down with the repository —
  no "works on my machine because I have Sass 1.105 and CI has 1.100."
- **Hash-verified.** NuGet checks the package against what it published, unlike a `curl | sh` installer.
- **No network at run time**, for the platform-specific package — it **contains the official Dart Sass
  bundle**, so there's no download on first use, no dependency on github.com being reachable, and no
  chance of a CI agent quietly picking up a different Sass than your laptop did.

## Install

Per repository (recommended — this is the part that pins):

```bash
dotnet new tool-manifest      # once per repository
dotnet tool install Scarlet.Sass.Cli
dotnet sass --version
```

Commit `.config/dotnet-tools.json` and every contributor and CI agent gets the same Dart Sass from
`dotnet tool restore`.

Globally:

```bash
dotnet tool install -g Scarlet.Sass.Cli
```

Or once, without installing anything (.NET 10 SDK):

```bash
dnx Scarlet.Sass.Cli -- Sass:wwwroot/css --style=compressed
```

> **The package version is the Dart Sass version.** `Scarlet.Sass.Cli` 1.104.1 contains Dart Sass 1.104.1,
> the same as the `Scarlet.Sass.Runtime.*` packages.

To move to a newer Dart Sass, update the package like any other .NET tool — no separate upgrade command
needed:

```bash
dotnet tool update Scarlet.Sass.Cli      # local: also bumps .config/dotnet-tools.json
dotnet tool update -g Scarlet.Sass.Cli   # global
```

## How it finds Dart Sass

`Scarlet.Sass.Cli` is a pointer package: it owns the `dotnet-sass` command but carries no Dart Sass bundle
itself. Installing it makes `dotnet tool install`/`dotnet tool restore` also pull one matching sub-package
for your machine's RID — `Scarlet.Sass.Cli.win-x64`, `Scarlet.Sass.Cli.linux-arm64`, and so on — and *that*
package embeds the official `dart-sass` folder. This is automatic; you never name a sub-package yourself,
and running `dotnet add package Scarlet.Sass.Cli.<rid>` on one directly installs nothing usable — it
carries no library assets, only a tool payload NuGet places when `Scarlet.Sass.Cli` asks for it.

**Supported platforms are Windows, Linux and macOS on x64 or arm64**, including musl-based Linux
distributions such as Alpine — one sub-package per combination, eight in total. Hosts outside that matrix
restore `Scarlet.Sass.Cli.any` instead: a portable fallback with no embedded runtime, so it downloads Dart
Sass on first use and caches it per user rather than shipping a mismatched one. Any other architecture gets
an explanatory error rather than a mismatched runtime — install Dart Sass separately and point at its
launcher with `SCARLET_SASS_PATH` if you need one.

Resolution order:

1. `SCARLET_SASS_PATH`, if set — errors if it points at nothing, rather than quietly falling back
2. the Dart Sass bundle embedded in the installed package
3. a previously downloaded Dart Sass bundle in the per-user cache
4. a download

To see what it chose and why:

```bash
dotnet sass --scarlet-info
dotnet sass --scarlet-info --json
```

That is the only argument the tool reserves for itself, it is recognised only as the *first* argument, and
`SCARLET_SASS_PASSTHROUGH=1` disables even that. It never downloads anything — it reports the URL it
*would* use.

## Configuration

| Variable | Effect |
|----------|--------|
| `SCARLET_SASS_PATH` | Use this Sass launcher. Highest precedence. |
| `SCARLET_SASS_VERSION` | Resolve a different Dart Sass version, or `latest`. Bypasses the embedded runtime. |
| `SCARLET_SASS_CACHE` | Override the download cache root. |
| `SCARLET_SASS_NO_EMBEDDED` | Ignore the embedded runtime. |
| `SCARLET_SASS_DIAGNOSTICS` | Print the resolved Sass launcher to stderr before running. |
| `SCARLET_SASS_PASSTHROUGH` | Disable `--scarlet-info` so every argument reaches Sass. |
| `SCARLET_SASS_DOWNLOAD_TIMEOUT` | Seconds to wait for a concurrent download. Defaults to 300. |

Configuration is environment variables rather than command-line flags on purpose: every argument belongs
to Dart Sass, so a flag Sass adds in future keeps working without a release of this package.

`SCARLET_SASS_VERSION` changes which **Dart Sass runtime** gets downloaded and run — it never changes which
**NuGet package** is installed; that's decided once, at `dotnet tool install` time (see
[How it finds Dart Sass](#how-it-finds-dart-sass)). Setting it to anything other than the version baked
into the installed package skips the embedded runtime and downloads the requested one into the per-user
cache, scoped by version (`<cache>/runtimes/<version>/`), so later runs with the same value reuse it instead
of re-downloading.

That caching applies to `latest` too, literally: the first run resolves whatever GitHub currently tags as
newest and caches it under a folder named `latest`, and every run after that reuses that cached runtime
without checking GitHub again — `latest` means "newest at the time I first asked," not "always current."
Clear `<cache>/runtimes/latest/` (or point `SCARLET_SASS_CACHE` elsewhere) to pick up a newer release.

## Running Dart Sass during a build instead

If you want Sass to run as part of `dotnet build` and have its CSS included in ASP.NET Core, Blazor or Razor
Class Library static web assets, use
[`Scarlet.Sass.MSBuild`](https://www.nuget.org/packages/Scarlet.Sass.MSBuild/) instead. The two are
independent; this tool is for the command line.

## Links

- [Source and full documentation](https://github.com/ScarletKuro/Scarlet.Sass)
- [Dart Sass documentation](https://sass-lang.com/documentation/cli/dart-sass/)

Licensed under MIT. Dart Sass itself is licensed separately — see the
[Dart Sass repository](https://github.com/sass/dart-sass).
