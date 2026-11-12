using System.ComponentModel;

namespace Scarlet.Sass.Cli.Watch;

/// <summary>
/// Runs every Sass compilation resolved from an MSBuild project as a foreground Dart Sass watcher.
/// </summary>
internal sealed class SassWatchCommand : ISassWatchCommand
{
    /// <summary>The .NET switch for hosts where file-system events are unreliable, honoured by dotnet watch.</summary>
    public const string PollingFileWatcherVariable = "DOTNET_USE_POLLING_FILE_WATCHER";

    private const string PollFlag = "--poll";

    private readonly ISassWatchConfigurationProvider _configurationProvider;
    private readonly IWatchProcessLauncher _launcher;
    private readonly IWatchSessionLockProvider _sessionLocks;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly Func<string> _getCurrentDirectory;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public SassWatchCommand(
        ISassWatchConfigurationProvider configurationProvider,
        IWatchProcessLauncher launcher,
        IWatchSessionLockProvider sessionLocks,
        TextWriter stdout,
        TextWriter stderr,
        Func<string> getCurrentDirectory,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _configurationProvider = configurationProvider;
        _launcher = launcher;
        _sessionLocks = sessionLocks;
        _stdout = stdout;
        _stderr = stderr;
        _getCurrentDirectory = getCurrentDirectory;
        _getEnvironmentVariable = getEnvironmentVariable ?? (static _ => null);
    }

    public int Run(string[] args)
    {
        if (!TryParse(args, out var options))
        {
            return ExitCodes.UsageError;
        }

        if (options.Help)
        {
            WriteHelp();
            return ExitCodes.Success;
        }

        try
        {
            var configuration = _configurationProvider.Resolve(
                options.Project,
                options.Configuration,
                _getCurrentDirectory());

            if (configuration.Count == 0)
            {
                _stderr.WriteLine("Scarlet.Sass: the project has no enabled SassBeforeStaticWebAssets inputs.");
                return ExitCodes.UsageError;
            }

            // Dart Sass only accepts --poll alongside --watch, so the watcher is the one place it can be added.
            if (options.Poll || IsPollingRequestedByEnvironment())
            {
                configuration = configuration
                    .Select(invocation => invocation with
                    {
                        ProcessArguments = invocation.ProcessArguments + " " + PollFlag
                    })
                    .ToArray();
            }

            if (!_sessionLocks.TryAcquire(configuration, out var sessionLock))
            {
                _stderr.WriteLine("Scarlet.Sass: a watcher is already running for one or more configured outputs.");
                return ExitCodes.Success;
            }

            using var sessionLease = sessionLock;
            var count = configuration.Sum(static invocation => invocation.InputPaths.Count);
            _stderr.WriteLine($"Scarlet.Sass: watching {count} Sass input(s)");

            foreach (var invocation in configuration)
            {
                for (var index = 0; index < invocation.InputPaths.Count; index++)
                {
                    _stderr.WriteLine($"Scarlet.Sass:   {invocation.InputPaths[index]} -> {invocation.OutputPaths[index]}");
                }
            }

            return _launcher.Run(configuration);
        }
        catch (SassWatchException exception)
        {
            _stderr.WriteLine($"Scarlet.Sass: {exception.Message}");
            return exception.ExitCode;
        }
        catch (Win32Exception exception)
        {
            _stderr.WriteLine($"Scarlet.Sass: failed to start a watch process: {exception.Message}");
            return ExitCodes.SassNotExecutable;
        }
    }

    private bool IsPollingRequestedByEnvironment()
    {
        var value = _getEnvironmentVariable(PollingFileWatcherVariable);

        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryParse(string[] args, out SassWatchOptions options)
    {
        string? project = null;
        var configuration = "Debug";
        var poll = false;
        var help = false;
        options = default;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (argument is "--help" or "-h")
            {
                help = true;
                continue;
            }

            if (argument == PollFlag)
            {
                poll = true;
                continue;
            }

            if (TryReadOption(args, ref index, argument, "--project", "-p", out var projectValue))
            {
                if (projectValue is null)
                {
                    return false;
                }

                project = projectValue;
                continue;
            }

            if (TryReadOption(args, ref index, argument, "--configuration", "-c", out var configurationValue))
            {
                if (configurationValue is null)
                {
                    return false;
                }

                configuration = configurationValue;
                continue;
            }

            _stderr.WriteLine($"Scarlet.Sass: unrecognised watch option '{argument}'.");
            return false;
        }

        options = new SassWatchOptions(project, configuration, poll, help);
        return true;
    }

    private bool TryReadOption(
        string[] args,
        ref int index,
        string argument,
        string longName,
        string shortName,
        out string? value)
    {
        value = null;

        if (argument.StartsWith(longName + "=", StringComparison.Ordinal))
        {
            value = argument[(longName.Length + 1)..];
        }
        else if (argument == longName || argument == shortName)
        {
            if (index + 1 >= args.Length)
            {
                _stderr.WriteLine($"Scarlet.Sass: {argument} requires a value.");
                return true;
            }

            value = args[++index];
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _stderr.WriteLine($"Scarlet.Sass: {longName} requires a non-empty value.");
            value = null;
        }

        return true;
    }

    private void WriteHelp()
    {
        _stdout.WriteLine("Usage: dotnet sass watch [options]");
        _stdout.WriteLine();
        _stdout.WriteLine("Runs Dart Sass watch mode using the inputs and settings from Scarlet.Sass.MSBuild.");
        _stdout.WriteLine();
        _stdout.WriteLine("Options:");
        _stdout.WriteLine("  -p, --project <path>        Project file or directory. Defaults to the current directory.");
        _stdout.WriteLine("  -c, --configuration <name> MSBuild configuration. Defaults to Debug.");
        _stdout.WriteLine("      --poll                 Poll instead of using file-system events. Implied when");
        _stdout.WriteLine($"                             {PollingFileWatcherVariable} is 1 or true.");
        _stdout.WriteLine("  -h, --help                 Show this help.");
    }

    private readonly record struct SassWatchOptions(
        string? Project,
        string Configuration,
        bool Poll,
        bool Help);
}
