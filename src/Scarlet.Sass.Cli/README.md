# Scarlet.Sass.Cli

Run the official Dart Sass CLI as a .NET tool:

```bash
dotnet new tool-manifest
dotnet tool install Scarlet.Sass.Cli
dotnet sass --version
dotnet sass Sass:wwwroot/css --style=compressed
```

Every argument is forwarded to Dart Sass verbatim. The only reserved argument is first-position `--scarlet-info`.

```bash
dotnet sass --scarlet-info
dotnet sass --scarlet-info --json
```

Set `SCARLET_SASS_PASSTHROUGH=1` if even `--scarlet-info` should be forwarded to Sass.

## Runtime Resolution

Resolution order:

1. `SCARLET_SASS_PATH`
2. Dart Sass embedded in the RID-specific tool package
3. Dart Sass in the per-user Scarlet cache
4. download from `https://github.com/sass/dart-sass`

The portable `any` tool package has no embedded Sass runtime and downloads on first use. RID-specific tool packages embed `dart-sass/sass` on Unix and `dart-sass/sass.bat` on Windows.

## Environment Variables

| Variable | Meaning |
| --- | --- |
| `SCARLET_SASS_PATH` | Use this Sass executable. Highest precedence. |
| `SCARLET_SASS_VERSION` | Download a different Sass version or `latest`. |
| `SCARLET_SASS_CACHE_DIR` | Override the cache root. |
| `SCARLET_SASS_NO_EMBEDDED` | Ignore the embedded runtime. |
| `SCARLET_SASS_DIAGNOSTICS` | Print the resolved Sass path before running. |
| `SCARLET_SASS_PASSTHROUGH` | Forward `--scarlet-info` instead of handling it. |
| `SCARLET_SASS_DOWNLOAD_TIMEOUT` | Seconds to wait for another process downloading Sass. |

## Notes

`Scarlet.Sass.Cli` is for direct command-line use. Use `Scarlet.Sass.MSBuild` when CSS should be produced during `dotnet build` and included in static web assets.

Scarlet.Sass does not define a JSON configuration file. Pass Dart Sass options as command-line arguments.

Scarlet.Sass is MIT licensed. Dart Sass is distributed under its own MIT license; see `LICENSE-3RD-PARTY.txt` in the package.
