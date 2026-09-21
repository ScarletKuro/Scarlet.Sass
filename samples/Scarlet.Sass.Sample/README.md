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
