using Scarlet.Sass.Core;

namespace Scarlet.Sass.Cli;

/// <summary>
/// A request to run Sass.
/// </summary>
/// <param name="Command">The process command used to start Sass.</param>
/// <param name="Arguments">The user arguments, forwarded verbatim and in order after <paramref name="Command"/> arguments.</param>
internal readonly record struct SassLaunchRequest(SassLaunchCommand Command, IReadOnlyList<string> Arguments);
