namespace Scarlet.Sass.Core;

/// <summary>
/// Where a <see cref="SassRuntimePack"/> came from.
/// </summary>
/// <remarks>
/// Reserved for future diagnostics if another runtime-pack source is added.
/// </remarks>
public enum SassRuntimePackSource
{
    /// <summary>Declared as a <c>SassRuntimePack</c> item. The supported contract.</summary>
    Item,

    /// <summary>Reserved; v1 does not expose a legacy <c>SassRuntime_&lt;rid&gt;</c> property contract.</summary>
    LegacyProperty
}
