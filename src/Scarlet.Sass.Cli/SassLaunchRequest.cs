namespace Scarlet.Sass.Cli;

/// <summary>
/// A request to run Sass.
/// </summary>
/// <param name="ExecutablePath">The Sass executable to start.</param>
/// <param name="Arguments">The arguments, forwarded verbatim and in order.</param>
internal readonly record struct SassLaunchRequest(string ExecutablePath, IReadOnlyList<string> Arguments);