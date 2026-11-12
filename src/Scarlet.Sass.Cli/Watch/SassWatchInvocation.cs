namespace Scarlet.Sass.Cli.Watch;

/// <summary>
/// One Dart Sass watch process, resolved from the project's build: the build's executable and its exact command
/// line for one settings group, with the watch flag added.
/// </summary>
/// <param name="ExecutablePath">The process to start - Dart Sass's own <c>dart</c> runtime when the bundle has one.</param>
/// <param name="ProcessArguments">
/// The command line as one string, passed to <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/> exactly as
/// the build passes it, so Dart Sass parses the same arguments the build gave it.
/// </param>
/// <param name="WorkingDirectory">The project directory, where the build runs Dart Sass from.</param>
/// <param name="DisplayPath">The Sass launcher this was resolved from, for messages.</param>
/// <param name="InputPaths">The compiled inputs, files or directories, in the order of the command line.</param>
/// <param name="OutputPaths">Where each input compiles to; what the session lock is held on.</param>
/// <param name="GeneratedPaths">The files the build currently expects these compilations to produce.</param>
internal readonly record struct SassWatchInvocation(
    string ExecutablePath,
    string ProcessArguments,
    string WorkingDirectory,
    string DisplayPath,
    IReadOnlyList<string> InputPaths,
    IReadOnlyList<string> OutputPaths,
    IReadOnlyList<string> GeneratedPaths);
