using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Scarlet.Sass.Core;

namespace Scarlet.Sass.Cli;

/// <summary>
/// Starts Sass as a child process and waits for it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is redirected. Sass inherits this process's stdin, stdout and stderr <em>handles</em>, so
/// <c>isatty</c> is true, colours and progress rendering work, and piping behaves exactly as it would when invoking Sass directly. Redirecting in order to
/// re-emit the output would break all of that.
/// </para>
/// <para>
/// The working directory is deliberately left unset so the child inherits ours verbatim. Dart Sass resolves
/// relative input/output paths, load paths, and import URLs from the caller's context.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class ProcessLauncher : IProcessLauncher
{
    private readonly ISassLogger _log;

    public ProcessLauncher(ISassLogger log) => _log = log;

    /// <inheritdoc />
    public int Run(SassLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo(request.Command.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        // ArgumentList, never a concatenated string: the runtime applies the platform's quoting rules, which
        // is the only way arguments containing spaces, quotes or trailing backslashes survive intact.
        foreach (var argument in request.Command.Arguments.Concat(request.Arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process();
        process.StartInfo = startInfo;

        // Shared with Scarlet.Sass.MSBuild.SassCompileTask: both start a Dart Sass process file that may
        // have just been downloaded, or just been run, by a step moments earlier, and can race the same
        // ETXTBSY window.
        ProcessStartRetry.Start(process, _log);

        using var signals = new ProcessSignalBridge([process]);

        process.WaitForExit();

        return process.ExitCode;
    }
}
