using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Scarlet.Sass.Core;
using Scarlet.Sass.Core.Providers;

namespace Scarlet.Sass.Cli;

/// <summary>
/// Composition root. Everything interesting lives in <see cref="SassCliApplication"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class Program
{
    // Returning the code from Main rather than assigning Environment.ExitCode means it is set after
    // finalizers have run, so a slow finalizer can never race the process exit.
    private static int Main(string[] args)
    {
        var fileSystem = new FileSystem();
        var environment = new SystemEnvironmentProvider();
        var chmodProvider = Chmod.CreateProvider();

        Platform platform;
        try
        {
            platform = SassRuntimeResolver.GetCurrentPlatform();
        }
        catch (PlatformNotSupportedException exception)
        {
            Console.Error.WriteLine($"Scarlet.Sass: {exception.Message}");

            return ExitCodes.SassNotFound;
        }

        var options = SassCliOptions.FromEnvironment(environment, SassBuildInfo.PinnedSassVersion);

        var resolver = new SassCliResolver(
            fileSystem,
            chmodProvider,
            platform,
            AppContext.BaseDirectory,
            (targetPlatform, log) => new SassDownloader(
                SassDownloader.CreateHttpClient(),
                new GitHubLatestVersionResolver(),
                fileSystem,
                ZipArchiveProvider.Instance,
                TarArchiveProvider.Instance,
                chmodProvider,
                targetPlatform,
                log));

        var application = new SassCliApplication(
            resolver,
            new ProcessLauncher(new ConsoleSassLogger(Console.Error)),
            options,
            Console.Out,
            Console.Error);

        return application.Run(args);
    }
}
