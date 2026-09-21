using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Serve static files from wwwroot
app.UseStaticFiles();

// Simple endpoint to verify the app is running
app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <title>Scarlet.Sass.Sample</title>
    <link rel=""stylesheet"" href=""/css/style.css"">
</head>
<body>
    <div class=""container"">
        <h1>Scarlet.Sass.Sample</h1>
        <p>This is a sample application demonstrating the Scarlet.Sass.MSBuild task.</p>
        <button class=""button"">Test Button</button>
    </div>
</body>
</html>
", "text/html"));

app.Run();
