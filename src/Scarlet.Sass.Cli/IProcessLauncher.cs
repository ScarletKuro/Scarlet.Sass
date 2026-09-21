namespace Scarlet.Sass.Cli;

/// <summary>
/// Runs Sass and returns its exit code.
/// </summary>
/// <remarks>
/// The seam that keeps everything else unit-testable: tests substitute a recorder and assert the exact
/// argument list that would have been handed to Sass, without starting a process.
/// </remarks>
internal interface IProcessLauncher
{
    /// <summary>
    /// Runs the request to completion.
    /// </summary>
    /// <param name="request">What to run.</param>
    /// <returns>Sass's exit code.</returns>
    int Run(SassLaunchRequest request);
}
