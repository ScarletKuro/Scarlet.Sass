# Scarlet.Sass.Sample.Download

Sample ASP.NET Core app that uses `SassRuntimeDownload=true` instead of referencing a platform runtime package.

```xml
<PropertyGroup>
  <SassRuntimeDownload>true</SassRuntimeDownload>
  <SassVersionDownload>1.104.1</SassVersionDownload>
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
