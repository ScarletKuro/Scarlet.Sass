namespace Scarlet.Sass.MSBuild.Tests.Mock;

internal sealed class NoOpSassLogger : ISassLogger
{
    public void LogMessage(string message) { }

    public static NoOpSassLogger Instance { get; } = new NoOpSassLogger();
}
