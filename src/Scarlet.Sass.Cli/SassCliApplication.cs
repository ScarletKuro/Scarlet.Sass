using System.ComponentModel;
using Scarlet.Sass.Core;

namespace Scarlet.Sass.Cli;

/// <summary>
/// Orchestrates a single invocation: resolve Sass, then hand it every argument untouched.
/// </summary>
internal sealed class SassCliApplication
{
    /// <summary>
    /// The one argument the tool reserves for itself.
    /// </summary>
    /// <remarks>
    /// Honoured only as the first argument, and only when <c>SCARLET_SASS_PASSTHROUGH</c> is unset. Sass would
    /// never ship a flag carrying a third party's brand, and restricting it to position 0 means
    /// <c>sass --watch --scarlet-info</c> still reaches Sass. The environment variable is a permanent
    /// opt-out should that reasoning ever fail.
    /// </remarks>
    public const string InfoFlag = "--scarlet-info";

    private const string JsonFlag = "--json";

    private readonly SassCliResolver _resolver;
    private readonly IProcessLauncher _launcher;
    private readonly SassCliOptions _options;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    /// <summary>
    /// Initializes a new instance of the <see cref="SassCliApplication"/> class.
    /// </summary>
    /// <param name="resolver">Finds the Sass launcher.</param>
    /// <param name="launcher">Runs it.</param>
    /// <param name="options">Configuration read from the environment.</param>
    /// <param name="stdout">Standard output, used only by diagnostics.</param>
    /// <param name="stderr">Standard error, used for tool messages so Sass's stdout stays clean.</param>
    public SassCliApplication(
        SassCliResolver resolver,
        IProcessLauncher launcher,
        SassCliOptions options,
        TextWriter stdout,
        TextWriter stderr)
    {
        _resolver = resolver;
        _launcher = launcher;
        _options = options;
        _stdout = stdout;
        _stderr = stderr;
    }

    /// <summary>
    /// Runs one invocation.
    /// </summary>
    /// <param name="args">The command line, forwarded verbatim unless it opens with <see cref="InfoFlag"/>.</param>
    /// <returns>Sass's exit code, or one of <see cref="ExitCodes"/> when Sass never ran.</returns>
    public int Run(string[] args)
    {
        if (IsInfoRequest(args))
        {
            return WriteDiagnostics(args);
        }

        var log = new ConsoleSassLogger(_stderr);

        SassResolution resolution;
        try
        {
            resolution = _resolver.Resolve(_options, allowDownload: true, log);
        }
        catch (Exception exception)
        {
            _stderr.WriteLine($"Scarlet.Sass: could not obtain Sass {_options.RequestedVersion}: {exception.Message}");

            return ExitCodes.SassNotFound;
        }

        if (!resolution.IsResolved)
        {
            _stderr.WriteLine($"Scarlet.Sass: {resolution.FailureReason}");

            return ExitCodes.SassNotFound;
        }

        if (_options.Diagnostics)
        {
            _stderr.WriteLine($"Scarlet.Sass: resolved Sass launcher {resolution.LauncherPath}");
            _stderr.WriteLine($"Scarlet.Sass: executing {SassCommandLine.FormatProcessCommand(resolution.LaunchCommand, args)}");
        }

        try
        {
            return _launcher.Run(new SassLaunchRequest(resolution.LaunchCommand, args));
        }
        catch (Win32Exception exception)
        {
            var command = resolution.LaunchCommand;
            _stderr.WriteLine($"Scarlet.Sass: failed to start Sass command '{command.FileName}' for '{command.DisplayPath}': {exception.Message}");

            return ExitCodes.SassNotExecutable;
        }
    }

    private bool IsInfoRequest(string[] args)
    {
        return !_options.PurePassthrough
               && args.Length > 0
               && string.Equals(args[0], InfoFlag, StringComparison.Ordinal);
    }

    private int WriteDiagnostics(string[] args)
    {
        var asJson = false;

        foreach (var argument in args.Skip(1))
        {
            if (string.Equals(argument, JsonFlag, StringComparison.Ordinal))
            {
                asJson = true;

                continue;
            }

            _stderr.WriteLine($"Scarlet.Sass: unrecognised option '{argument}' after {InfoFlag}.");

            return ExitCodes.UsageError;
        }

        // allowDownload: false - asking the tool what it would do must never itself fetch 90 MB.
        var resolution = _resolver.Resolve(_options, allowDownload: false, new ConsoleSassLogger(_stderr));

        _stdout.Write(asJson
            ? DiagnosticsReport.ToJson(resolution, _options)
            : DiagnosticsReport.ToText(resolution, _options));

        return ExitCodes.Success;
    }
}
