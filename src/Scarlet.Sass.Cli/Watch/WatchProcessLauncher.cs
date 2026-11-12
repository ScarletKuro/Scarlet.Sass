using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Scarlet.Sass.Core;

namespace Scarlet.Sass.Cli.Watch;

/// <summary>
/// Starts every configured Dart Sass watcher in the current foreground console and owns their lifetime.
/// </summary>
internal sealed class WatchProcessLauncher : IWatchProcessLauncher
{
    private readonly ISassLogger _log;

    public WatchProcessLauncher(ISassLogger log) => _log = log;

    [ExcludeFromCodeCoverage]
    public int Run(IEnumerable<SassWatchInvocation> invocations)
    {
        var processes = new List<Process>();

        try
        {
            foreach (var invocation in invocations)
            {
                var process = new Process { StartInfo = CreateStartInfo(invocation) };
                ProcessStartRetry.Start(process, _log);
                processes.Add(process);
            }

            using var signals = new ProcessSignalBridge(processes);
            var exits = processes.Select(static process => process.WaitForExitAsync()).ToArray();
            var firstExit = Task.WhenAny(exits).GetAwaiter().GetResult();
            var firstIndex = Array.IndexOf(exits, firstExit);

            // One watcher ending means the session is no longer complete. Take down the rest so the command
            // never leaves a partially functioning or orphaned watch session behind.
            signals.KillRunningProcesses();
            Task.WhenAll(exits).GetAwaiter().GetResult();

            return processes[firstIndex].ExitCode;
        }
        finally
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception)
                {
                    // Already gone or inaccessible; disposal is still safe.
                }

                process.Dispose();
            }
        }
    }

    /// <remarks>
    /// The command line goes through <see cref="ProcessStartInfo.Arguments"/> as the single string the build
    /// produced rather than being re-split into <see cref="ProcessStartInfo.ArgumentList"/>: the build launches
    /// Dart Sass the same way, so both see identical arguments, quoting included.
    /// </remarks>
    internal static ProcessStartInfo CreateStartInfo(SassWatchInvocation invocation)
    {
        return new ProcessStartInfo(invocation.ExecutablePath, invocation.ProcessArguments)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = invocation.WorkingDirectory
        };
    }
}
