# End-to-End (E2E) Tests

This directory contains end-to-end tests that validate the complete Scarlet.Sass package installation and execution flow.

The tests intentionally exercise packed NuGet packages from a local feed instead of project references. That is what catches the expensive failures: missing task dependencies in `tools/netstandard2.0/`, broken `build/` or `buildMultiTargeting/` imports, runtime package layout mistakes, static web asset timing regressions, and CLI pointer/RID package issues.

## Directory Structure

```text
tests/e2e/
├── package-installation/
│   ├── verify.sh                    # Package installation and static web assets E2E test
│   └── templates/                   # Template files for the package install test
├── monorepo-download/
│   ├── verify.sh                    # Shared runtime download E2E test
│   └── templates/                   # Template files for the monorepo download test
├── multi-tfm/
│   ├── verify.sh                    # Multi-target framework Razor Class Library E2E test
│   └── templates/                   # Template files for the multi-TFM test
├── incremental/
│   ├── verify.sh                    # Dart Sass --update and clean E2E test
│   └── templates/                   # Template files for the incremental test
├── cli-tool/
│   ├── verify.sh                    # dotnet sass .NET tool E2E test
│   └── templates/                   # Template files for the CLI tool test
└── README.md                        # This file
```

## Overview

The E2E tests verify that:

1. NuGet packages can be created and installed from a local source.
2. `Scarlet.Sass.MSBuild` loads as a packaged MSBuild task with all required task dependencies beside it.
3. Runtime packages expose `@(SassRuntimePack)` from both `build/` and `buildMultiTargeting/`.
4. The resolver selects the host Dart Sass runtime package, not a target framework or target RID runtime.
5. Explicit `@(SassBeforeStaticWebAssets)` items compile before Blazor/static web assets are resolved.
6. Generated CSS and source maps are injected into `@(Content)` and `@(FileWrites)`.
7. Razor Class Libraries pack generated CSS under `staticwebassets/`.
8. Multi-targeted projects run Sass once in the outer build, not once per TFM.
9. Dart Sass `--update` keeps unchanged outputs untouched.
10. Scarlet.Sass clean/stamp/manifest behavior removes generated files when it should.
11. `SassRuntimeDownload=true` works with a shared runtime directory.
12. `dotnet sass` installs from the local feed, selects its RID-specific package, uses the embedded Dart Sass runtime, and forwards Dart Sass arguments.

## Runtime Package Selection

Dart Sass runs on the build host. The scripts therefore use the current `dotnet --info` RID to pick the runtime package:

| `dotnet --info` RID | Runtime package |
| --- | --- |
| `win-x64` | `Scarlet.Sass.Runtime.windows-x64` |
| `win-arm64` | `Scarlet.Sass.Runtime.windows-arm64` |
| `linux-x64` | `Scarlet.Sass.Runtime.linux-x64` |
| `linux-arm64` | `Scarlet.Sass.Runtime.linux-arm64` |
| `linux-musl-x64` | `Scarlet.Sass.Runtime.linux-x64-musl` |
| `linux-musl-arm64` | `Scarlet.Sass.Runtime.linux-arm64-musl` |
| `osx-x64` | `Scarlet.Sass.Runtime.darwin-x64` |
| `osx-arm64` | `Scarlet.Sass.Runtime.darwin-arm64` |

Unsupported RIDs fail fast with a clear error. The scripts log the detected RID and selected runtime package before restore/build so CI failures show the selected path immediately.

## Template System

The tests use checked-in templates instead of generating project/assets inline. Template files live in each scenario's `templates/` directory and are copied or processed into temporary projects during execution.

Common placeholders:

- `{{WORKSPACE_PATH}}`: Repository root containing the `packages` folder.
- `{{PACKAGE_VERSION}}`: `Scarlet.Sass.MSBuild` package version under test.
- `{{RUNTIME_PACKAGE}}`: Host-specific runtime package selected by the script.
- `{{RUNTIME_VERSION}}`: Dart Sass runtime package version under test.
- `{{Sass_VERSION}}`: Dart Sass version used by download-mode tests.
- `{{SHARED_RUNTIME_DIR}}`: Shared download directory for monorepo tests.
- `{{CLASS_NAME}}`: Per-app selector used by monorepo Sass fixtures.

The scripts escape Windows backslashes before running `sed`, so paths like `D:\a\repo` are safe under Git Bash.

## Test Scripts

### package-installation/verify.sh

**Purpose**: validates that `Scarlet.Sass.MSBuild` works correctly when installed from a local NuGet source with a host runtime package.

**Usage**:

```bash
./tests/e2e/package-installation/verify.sh <workspace-path> <package-version> <runtime-version>
```

**Arguments**:

- `workspace-path`: Repository root containing the `packages` folder.
- `package-version`: Version of `Scarlet.Sass.MSBuild` to test, for example `1.0.0-local`.
- `runtime-version`: Version of the runtime packages to test, for example `1.104.1`.

**What it does**:

1. Creates a temporary test directory.
2. Creates `nuget.config` from template with the local package source.
3. Creates an ASP.NET Core web project.
4. Replaces the project file with a template referencing `Scarlet.Sass.MSBuild` and the selected runtime package.
5. Copies checked-in Sass fixtures into `Sass/_variables.scss` and `Sass/site.scss`.
6. Restores from the local feed.
7. Builds with normal verbosity so runtime resolution messages are present in `build.log`.
8. Verifies `wwwroot/css/site.css` and `wwwroot/css/site.css.map` exist.
9. Verifies nested Sass syntax compiled into `.package-installation:hover`.
10. Verifies the build log reports resolution through the selected `SassRuntimePack`.
11. Publishes with `--no-build --configuration Debug` and verifies generated CSS is in publish output.
12. Runs `dotnet clean` and verifies generated CSS/map outputs were removed.

**Why it matters**: this scenario proves the packaged MSBuild task, runtime package item contract, Sass compilation, static web asset timing, content injection, and clean integration all work together in a real consuming project.

### multi-tfm/verify.sh

**Purpose**: validates the Razor Class Library multi-targeting case. This is central for Blazor/RCL users because the Sass step must run once before inner builds dispatch.

**Usage**:

```bash
./tests/e2e/multi-tfm/verify.sh <workspace-path> <package-version> <runtime-version>
```

**What it does**:

1. Creates a Razor Class Library.
2. Replaces the project file with a multi-targeted template.
3. Copies checked-in Sass fixtures into `Sass/`.
4. Builds with normal verbosity and captures `build.log`.
5. Verifies `wwwroot/css/site.css` exists.
6. Counts `Executing:` and requires exactly one invocation.
7. Packs with `--no-build --configuration Debug`.
8. Opens the `.nupkg` and verifies generated CSS appears under `staticwebassets/css/site.css`.

**Regression it catches**: if `RunSassBeforeStaticWebAssets` stops running in the outer build, runs once per TFM, or stops feeding static web assets, this test should fail.

### incremental/verify.sh

**Purpose**: validates the intended incremental contract.

Dart Sass owns Sass dependency freshness through `--update`. Scarlet.Sass owns the MSBuild-facing parts: settings/runtime stamp, generated manifest, stale output deletion, clean, and static web asset injection.

**Usage**:

```bash
./tests/e2e/incremental/verify.sh <workspace-path> <package-version> <runtime-version>
```

**What it does**:

1. Creates a Razor Class Library.
2. Copies checked-in Sass fixtures into `Sass/`.
3. First build: verifies CSS output exists.
4. Second build without changes: records CSS mtime before/after and verifies Dart Sass did not rewrite it.
5. Appends checked-in `update.scss.template` to `Sass/site.scss`.
6. Third build: verifies CSS mtime changed and `.incremental-updated` exists.
7. Runs `dotnet clean` and verifies generated CSS is removed.

**Regression it catches**: accidental always-rebuild behavior, broken `--update` usage, missing generated file clean wiring, and stamp/manifest mistakes.

### monorepo-download/verify.sh

**Purpose**: validates `SassRuntimeDownload=true` with a shared runtime directory across multiple projects.

This scenario intentionally does not install a `Scarlet.Sass.Runtime.*` package. It tests the download path and shared cache behavior instead.

**Usage**:

```bash
./tests/e2e/monorepo-download/verify.sh <workspace-path> <package-version> <sass-version>
```

**What it does**:

1. Creates a temporary solution with `App1` and `App2`.
2. Creates `Directory.Build.props` from template with `SassRuntimeDownload=true`, `SassVersionDownload`, and `SassRuntimeDirectory`.
3. Creates both web projects from templates.
4. Copies checked-in Sass fixtures into each app, substituting a different CSS class per app.
5. Builds the solution.
6. Verifies both apps generated `wwwroot/css/site.css`.
7. Verifies the shared runtime directory contains a Dart Sass launcher.

**Regression it catches**: broken download URL/archive mapping, shared download mutex issues, partial runtime publication, and task-side host platform detection errors.

### cli-tool/verify.sh

**Purpose**: proves that the `Scarlet.Sass.Cli` .NET tool runs Dart Sass from its embedded RID-specific package, with no runtime download.

**Usage**:

```bash
./tests/e2e/cli-tool/verify.sh <workspace-path> <package-version> <runtime-version>
```

`package-version` is accepted for a uniform E2E signature. The CLI package version follows `$(SassVersion).$(SassCliRevision)`, so when `SassCliRevision=0` NuGet normalizes it to the Dart Sass version, for example `1.104.1`.

**What it does**:

1. Creates a local tool manifest.
2. Creates `nuget.config` that points to the local package source.
3. Uses a private `NUGET_PACKAGES` directory so the test can see exactly what was restored.
4. Uses an empty `SCARLET_SASS_CACHE_DIR` that should never be created.
5. Installs `Scarlet.Sass.Cli`.
6. Verifies the RID-specific CLI package directory exists.
7. Verifies the package contains an embedded `sass` or `sass.bat` launcher.
8. Runs `dotnet sass --version` and checks Dart Sass reports the expected version.
9. Runs `dotnet sass --scarlet-info` and checks `Source ... embedded`.
10. Copies checked-in Sass fixtures into `Sass/`.
11. Runs `dotnet sass Sass/input.scss:out/input.css --style=compressed --no-source-map`.
12. Verifies compiled CSS exists and contains nested selector output.
13. Verifies the download cache directory was never created.

Checks 7, 9, and 13 carry the no-download claim together: the package contains Dart Sass, the tool says it used the embedded runtime, and the only directory it could have downloaded into does not exist.

**Contract it depends on**: `--scarlet-info` must render a `Source` line containing `embedded`. If that wording changes, update this script in the same commit.

## Running Locally

Pack the packages first:

```bash
dotnet pack src/Scarlet.Sass.MSBuild/Scarlet.Sass.MSBuild.csproj --configuration Release -o ./packages /p:PackageVersion=1.0.0-local

dotnet pack src/Scarlet.Sass.Runtime.windows-x64/Scarlet.Sass.Runtime.windows-x64.csproj --configuration Release -o ./packages /p:PackageVersion=1.104.1
dotnet pack src/Scarlet.Sass.Runtime.windows-arm64/Scarlet.Sass.Runtime.windows-arm64.csproj --configuration Release -o ./packages /p:PackageVersion=1.104.1
dotnet pack src/Scarlet.Sass.Runtime.linux-x64/Scarlet.Sass.Runtime.linux-x64.csproj --configuration Release -o ./packages /p:PackageVersion=1.104.1
dotnet pack src/Scarlet.Sass.Runtime.linux-arm64/Scarlet.Sass.Runtime.linux-arm64.csproj --configuration Release -o ./packages /p:PackageVersion=1.104.1
dotnet pack src/Scarlet.Sass.Runtime.linux-x64-musl/Scarlet.Sass.Runtime.linux-x64-musl.csproj --configuration Release -o ./packages /p:PackageVersion=1.104.1
dotnet pack src/Scarlet.Sass.Runtime.linux-arm64-musl/Scarlet.Sass.Runtime.linux-arm64-musl.csproj --configuration Release -o ./packages /p:PackageVersion=1.104.1
dotnet pack src/Scarlet.Sass.Runtime.darwin-x64/Scarlet.Sass.Runtime.darwin-x64.csproj --configuration Release -o ./packages /p:PackageVersion=1.104.1
dotnet pack src/Scarlet.Sass.Runtime.darwin-arm64/Scarlet.Sass.Runtime.darwin-arm64.csproj --configuration Release -o ./packages /p:PackageVersion=1.104.1

dotnet pack src/Scarlet.Sass.Cli/Scarlet.Sass.Cli.csproj --configuration Release -o ./packages
```

Then run one or more scripts:

```bash
tests/e2e/package-installation/verify.sh "$PWD" 1.0.0-local 1.104.1
tests/e2e/cli-tool/verify.sh "$PWD" 1.0.0-local 1.104.1
tests/e2e/multi-tfm/verify.sh "$PWD" 1.0.0-local 1.104.1
tests/e2e/incremental/verify.sh "$PWD" 1.0.0-local 1.104.1
tests/e2e/monorepo-download/verify.sh "$PWD" 1.0.0-local 1.104.1
```

On Windows, run these through Git Bash or another Bash-compatible shell. The scripts use `dotnet`, `sed`, `grep`, `find`, and `unzip`.

## Running In CI

A full CI run should:

1. Build and test the solution.
2. Pack `Scarlet.Sass.MSBuild`.
3. Pack all eight `Scarlet.Sass.Runtime.*` packages.
4. Pack `Scarlet.Sass.Cli`, including RID-specific packages and the pointer package.
5. Run `package-installation` on every matrix leg.
6. Run `multi-tfm` on every matrix leg.
7. Run `incremental` at least once per OS family.
8. Run `monorepo-download` at least once per OS family.
9. Run `cli-tool` on every matrix leg.

The scripts skip cleanup when `$CI` is set so failed runs leave their temporary projects available for inspection. Locally, temporary directories are deleted after success.

## Troubleshooting

**Package not found**

- Ensure packages were created in `./packages`.
- Ensure `package-version` matches the packed `Scarlet.Sass.MSBuild` version.
- Ensure `runtime-version` matches the packed runtime and CLI versions.

**Unsupported RID**

- Run `dotnet --info` and inspect the `RID:` line.
- Make sure the matching `Scarlet.Sass.Runtime.*` package was packed.
- Alpine/musl hosts should use the `linux-*-musl` runtime package names.

**Sass did not run**

- Inspect `build.log` for `Sass runtime pack`, `Using Sass at`, and `Executing:`.
- Confirm the project has explicit `@(SassBeforeStaticWebAssets)` items. There is no implicit `Sass/` folder convention.
- Confirm the runtime package contributes `@(SassRuntimePack)` from `build/` and `buildMultiTargeting/`.

**Static web assets are missing**

- Check that generated CSS is under `wwwroot/`.
- Check that `RunSassBeforeStaticWebAssets` ran before `ResolveProjectStaticWebAssets`.
- For RCL packages, inspect the `.nupkg` for `staticwebassets/`.

**Incremental output rewrites unexpectedly**

- Check that Dart Sass is invoked with `--update`.
- Check whether the settings/runtime stamp changed.
- Check whether the output manifest was deleted or considered stale.

**Download mode fails**

- Verify network access to the official Dart Sass release archives.
- Check `SassVersionDownload`.
- Inspect the shared runtime directory printed by the script.

**CLI uses download instead of embedded**

- Run `dotnet sass --scarlet-info`.
- Ensure the RID-specific CLI package was restored.
- Ensure `SCARLET_SASS_PASSTHROUGH` is not set to `1` when expecting Scarlet diagnostics.
