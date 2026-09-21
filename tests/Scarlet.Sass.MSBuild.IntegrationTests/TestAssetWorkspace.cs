using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

internal sealed class TestAssetWorkspace : IDisposable
{
    public string RootDirectory { get; }

    public string BuildScriptPath => Path.Combine(RootDirectory, "build.mjs");

    public string OutputDirectory => Path.Combine(RootDirectory, "output");

    private TestAssetWorkspace(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public static TestAssetWorkspace Create(ITestOutputHelper output)
    {
        var sourceDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets");
        var workspaceDirectory = Path.Combine(
            Path.GetTempPath(),
            $"scarlet-Sass-test-assets-{Guid.NewGuid():N}");

        CopyDirectory(sourceDirectory, workspaceDirectory);
        output.WriteLine($"Test assets directory: {workspaceDirectory}");

        return new TestAssetWorkspace(workspaceDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test workspaces.
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var filePath in Directory.GetFiles(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, overwrite: true);
        }

        foreach (var childDirectory in Directory.GetDirectories(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(childDirectory));
            CopyDirectory(childDirectory, destinationPath);
        }
    }
}
