namespace Scarlet.Sass.Cli.Watch;

internal sealed class SassWatchException : Exception
{
    public SassWatchException(string message, int exitCode = ExitCodes.UsageError)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
