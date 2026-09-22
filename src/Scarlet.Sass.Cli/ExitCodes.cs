namespace Scarlet.Sass.Cli;

/// <summary>
/// Exit codes the tool produces itself.
/// </summary>
/// <remarks>
/// Sass's own exit code is always returned verbatim when Sass actually ran, so these values only appear when
/// the tool failed before reaching Sass. They follow the shell convention so that scripts already handling
/// "command not found" behave sensibly without knowing anything about this tool.
/// </remarks>
internal static class ExitCodes
{
    /// <summary>Sass was resolved and started successfully; the reported code came from Sass.</summary>
    public const int Success = 0;

    /// <summary>The command line was not understood by the tool itself.</summary>
    public const int UsageError = 64;

    /// <summary>A resolved Sass launch command was found but could not be started.</summary>
    public const int SassNotExecutable = 126;

    /// <summary>No Sass runtime could be resolved.</summary>
    public const int SassNotFound = 127;
}
