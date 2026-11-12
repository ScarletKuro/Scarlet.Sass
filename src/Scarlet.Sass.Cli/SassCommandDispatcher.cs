using Scarlet.Sass.Cli.Watch;

namespace Scarlet.Sass.Cli;

/// <summary>
/// Reserves the project-aware <c>watch</c> command while preserving the CLI's raw Dart Sass passthrough contract.
/// </summary>
/// <remarks>
/// Only the bare word in first position is reserved. Dart Sass's own <c>--watch</c> flag is unaffected, so
/// <c>dotnet sass --watch in:out</c> still reaches Dart Sass verbatim, and <c>SCARLET_SASS_PASSTHROUGH=1</c>
/// gives up the reservation entirely.
/// </remarks>
internal sealed class SassCommandDispatcher
{
    public const string WatchCommand = "watch";

    private readonly SassCliApplication _sass;
    private readonly ISassWatchCommand _watch;
    private readonly bool _purePassthrough;

    public SassCommandDispatcher(SassCliApplication sass, ISassWatchCommand watch, bool purePassthrough)
    {
        _sass = sass;
        _watch = watch;
        _purePassthrough = purePassthrough;
    }

    public int Run(string[] args)
    {
        if (!_purePassthrough
            && args.Length > 0
            && string.Equals(args[0], WatchCommand, StringComparison.Ordinal))
        {
            return _watch.Run(args.Skip(1).ToArray());
        }

        return _sass.Run(args);
    }
}
