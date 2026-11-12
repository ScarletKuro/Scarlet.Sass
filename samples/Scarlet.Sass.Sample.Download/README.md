# Scarlet.Sass.Sample.Download

Sample ASP.NET Core app that uses `SassRuntimeDownload=true` instead of referencing a platform runtime package.

The sample itself sets `SassVersionDownload` to `$(SassVersion)`, the version this repository pins in
`Directory.Build.props`, so it always builds against the Dart Sass the packages ship. In your own project, write
the version out as below.

```xml
<PropertyGroup>
  <SassRuntimeDownload>true</SassRuntimeDownload>
  <SassVersionDownload>1.105.2</SassVersionDownload>
  <SassRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</SassRuntimeDirectory>
</PropertyGroup>
```

The project compiles `assets/styles` to `wwwroot/css` before static web assets are discovered.

Run:

```bash
dotnet build
dotnet run
```

Expected generated files:

- `wwwroot/css/style.css`
- `wwwroot/css/style.css.map`
