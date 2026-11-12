namespace Scarlet.Sass.Cli.Watch;

internal interface IWatchProcessLauncher
{
    int Run(IEnumerable<SassWatchInvocation> invocations);
}
