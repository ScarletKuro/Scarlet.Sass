namespace Scarlet.Sass.Cli;

/// <summary>
/// Where the Sass launcher came from.
/// </summary>
internal enum SassSource
{
    /// <summary>No Sass launcher could be resolved.</summary>
    NotFound,

    /// <summary>Supplied by the user through <c>SCARLET_SASS_PATH</c>.</summary>
    Explicit,

    /// <summary>Shipped inside this package. No network was involved, ever.</summary>
    Embedded,

    /// <summary>Found in the per-user download cache from an earlier run.</summary>
    Cache,

    /// <summary>Downloaded from GitHub during this run.</summary>
    Downloaded
}
