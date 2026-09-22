using Scarlet.Sass.Core;

namespace Scarlet.Sass.Cli;

/// <summary>
/// Writes the downloader's progress messages to stderr.
/// </summary>
/// <remarks>
/// stderr rather than stdout on purpose: <c>dotnet sass ... | jq</c> has to keep working, so nothing this
/// tool says may ever appear in Sass's output stream.
/// </remarks>
internal sealed class ConsoleSassLogger : ISassLogger
{
    private readonly TextWriter _stderr;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleSassLogger"/> class.
    /// </summary>
    /// <param name="stderr">The stream to write to.</param>
    public ConsoleSassLogger(TextWriter stderr) => _stderr = stderr;

    /// <inheritdoc />
    public void LogMessage(string message) => _stderr.WriteLine($"Scarlet.Sass: {message}");
}
