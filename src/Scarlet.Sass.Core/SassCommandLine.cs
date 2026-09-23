using System.Collections.Generic;
using System.Linq;

namespace Scarlet.Sass.Core;

/// <summary>
/// Formats Sass launch commands for process startup and diagnostics.
/// </summary>
public static class SassCommandLine
{
    public static string BuildProcessArguments(SassLaunchCommand command, string sassArguments) =>
        string.Join(" ", command.Arguments.Select(QuoteArgument).Concat(new[] { sassArguments }));

    public static string FormatProcessCommand(SassLaunchCommand command, string processArguments)
    {
        var commandText = QuoteArgument(command.FileName);
        return string.IsNullOrWhiteSpace(processArguments)
            ? commandText
            : $"{commandText} {processArguments}";
    }

    public static string FormatProcessCommand(SassLaunchCommand command, IEnumerable<string> userArguments)
    {
        var processArguments = string.Join(
            " ",
            command.Arguments.Concat(userArguments).Select(QuoteArgument));

        return FormatProcessCommand(command, processArguments);
    }

    public static string QuoteArgument(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
