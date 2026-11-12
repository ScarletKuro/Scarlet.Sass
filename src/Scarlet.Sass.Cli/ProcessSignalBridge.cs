using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Scarlet.Sass.Cli;

/// <summary>
/// Keeps the wrapper alive until its Sass processes exit, and makes sure they hear about termination.
/// </summary>
/// <remarks>
/// Two failure modes this exists to prevent: the wrapper dying first, which returns the shell prompt while Sass is
/// still drawing to the terminal; and Sass being orphaned, which .NET 10 made easier by removing the runtime's
/// default SIGTERM and SIGHUP handling. Shared by the passthrough launcher (one process) and the project-aware
/// watcher (one process per settings group).
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class ProcessSignalBridge : IDisposable
{
    private readonly IReadOnlyList<(Process Process, bool SharesProcessGroup)> _processes;
    private readonly List<PosixSignalRegistration> _registrations = new();
    private readonly EventHandler _processExitHandler;

    public ProcessSignalBridge(IReadOnlyList<Process> processes)
    {
        _processes = processes
            .Select(process => (process, PosixInterop.SharesProcessGroup(process.Id)))
            .ToArray();

        Register(PosixSignal.SIGINT, OnInterrupt);
        Register(PosixSignal.SIGQUIT, OnInterrupt);
        Register(PosixSignal.SIGTERM, OnTerminate);
        Register(PosixSignal.SIGHUP, OnTerminate);

        // Last resort for paths that bypass the handlers entirely.
        _processExitHandler = (_, _) => KillRunningProcesses();
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;

        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    public void KillRunningProcesses()
    {
        foreach (var (process, _) in _processes)
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
                // Already gone, or we lost the right to signal it. Either way there is nothing to do.
            }
        }
    }

    private void Register(PosixSignal signal, Action<PosixSignalContext> handler)
    {
        try
        {
            _registrations.Add(PosixSignalRegistration.Create(signal, handler));
        }
        catch (Exception)
        {
            // Not every signal is supported on every host. Losing one handler must not stop the tool from running
            // Sass at all.
        }
    }

    // Ctrl+C and Ctrl+Break are delivered by the OS to the whole console / foreground process group, so Sass
    // already got it. Killing it here would rob it of a graceful shutdown; exiting here would hand the prompt back
    // while it is still running. So: cancel our own termination and keep waiting.
    private void OnInterrupt(PosixSignalContext context)
    {
        context.Cancel = true;

        foreach (var (process, sharesProcessGroup) in _processes)
        {
            if (!sharesProcessGroup && IsRunning(process))
            {
                PosixInterop.Send(process.Id, PosixInterop.SIGINT);
            }
        }
    }

    // SIGTERM and SIGHUP are sent to this process alone, so they have to be forwarded explicitly.
    private void OnTerminate(PosixSignalContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            // Cancellation is not honoured for SIGTERM on Windows; this is the last chance to take Sass down with
            // us rather than orphan it.
            KillRunningProcesses();
            return;
        }

        context.Cancel = true;
        var signal = context.Signal == PosixSignal.SIGHUP ? PosixInterop.SIGHUP : PosixInterop.SIGTERM;

        foreach (var (process, _) in _processes)
        {
            if (IsRunning(process))
            {
                PosixInterop.Send(process.Id, signal);
            }
        }
    }

    // Once a child has been reaped its id can be reused by an unrelated process, so a session with several
    // watchers must not signal the ones that already exited.
    private static bool IsRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
