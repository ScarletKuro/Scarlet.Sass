# Scarlet.Sass.Sample

Sample ASP.NET Core app that compiles `assets/styles` to `wwwroot/css` before static web assets are discovered.

```xml
<SassBeforeStaticWebAssets Include="assets\styles">
  <OutputPath>wwwroot\css</OutputPath>
  <OutputStyle>Expanded</OutputStyle>
  <SourceMap>true</SourceMap>
</SassBeforeStaticWebAssets>
```

Run:

```bash
dotnet build
dotnet run
```

Expected generated files:

- `wwwroot/css/style.css`
- `wwwroot/css/style.css.map`

## Watching

`dotnet watch` does not know about `.scss` files on its own. Add a `Watch` item pointing at the sources:

```xml
<ItemGroup>
  <Watch Include="assets\styles\**\*.scss" />
</ItemGroup>
```

Then `dotnet watch run --no-hot-reload` (or `dotnet watch build`) rebuilds — and so re-runs Sass — whenever a stylesheet or partial under `assets/styles` changes.

With Hot Reload, the default for `dotnet watch run`, nothing is rebuilt, so the CSS would not change. Run Dart Sass's own watcher alongside instead; the app serves the new CSS without a restart:

```bash
dotnet watch run
dotnet sass --watch assets/styles:wwwroot/css
```

> **Do not put `--watch` in `SassAdditionalArguments`.** Dart Sass's watch mode never exits, so the build hangs instead of finishing. Use a `Watch` item as above, or run `dotnet sass --watch` as a separate process.

Point the glob at `assets/styles`, not `wwwroot` — watching the generated CSS would make every rebuild trigger another rebuild. See [dotnet watch Integration](../../src/Scarlet.Sass.MSBuild/README.md#dotnet-watch-integration) for the full details.
