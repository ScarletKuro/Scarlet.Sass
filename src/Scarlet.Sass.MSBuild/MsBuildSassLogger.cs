using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Scarlet.Sass.Core;

namespace Scarlet.Sass.MSBuild;

internal sealed class MsBuildSassLogger : ISassLogger
{
    private readonly TaskLoggingHelper _log;

    public MsBuildSassLogger(TaskLoggingHelper log) => _log = log;

    public void LogMessage(string message) =>
        _log.LogMessage(MessageImportance.High, message);
}
