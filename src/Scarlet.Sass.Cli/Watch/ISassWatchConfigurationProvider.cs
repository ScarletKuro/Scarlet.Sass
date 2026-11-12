namespace Scarlet.Sass.Cli.Watch;

internal interface ISassWatchConfigurationProvider
{
    IReadOnlyList<SassWatchInvocation> Resolve(string? project, string configuration, string currentDirectory);
}
