namespace Scarlet.Sass.Cli.Tests.Mock;

/// <summary>
/// Captures what would have been handed to Sass, so argument fidelity can be asserted without a process.
/// </summary>
internal sealed class RecordingProcessLauncher : IProcessLauncher
{
    private readonly int _exitCode;

    public RecordingProcessLauncher(int exitCode = 0) => _exitCode = exitCode;

    /// <summary>The request the launcher received, or <see langword="null"/> when it was never invoked.</summary>
    public SassLaunchRequest? Received { get; private set; }

    /// <summary>The arguments the launcher received, or an empty list when it was never invoked.</summary>
    public IReadOnlyList<string> ReceivedArguments => Received?.Arguments ?? Array.Empty<string>();

    public int Run(SassLaunchRequest request)
    {
        Received = request;

        return _exitCode;
    }
}
