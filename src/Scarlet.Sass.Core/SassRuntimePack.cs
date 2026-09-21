using System;
using System.Collections.Generic;
using System.IO;

namespace Scarlet.Sass.Core;

/// <summary>
/// A Sass runtime pack contributed to the build through the <c>SassRuntimePack</c> MSBuild item.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Scarlet.Sass.Runtime.*</c> package declares one of these in its <c>build/*.props</c>:
/// </para>
/// <code>
/// &lt;ItemGroup&gt;
///   &lt;SassRuntimePack Include="Scarlet.Sass.Runtime.darwin-arm64"&gt;
///     &lt;Rid&gt;osx-arm64&lt;/Rid&gt;
///     &lt;RuntimesPath&gt;$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)..', 'runtimes'))&lt;/RuntimesPath&gt;
///     &lt;Variant&gt;official&lt;/Variant&gt;
///     &lt;Priority&gt;0&lt;/Priority&gt;
///   &lt;/SassRuntimePack&gt;
/// &lt;/ItemGroup&gt;
/// </code>
/// <para>
/// The contract is an item rather than a fixed set of <c>SassRuntime_&lt;rid&gt;</c> properties so that a new
/// runtime identifier, alternate build, or locally compiled Sass can be added
/// without any change to <c>Scarlet.Sass.MSBuild</c> itself.
/// </para>
/// </remarks>
public sealed class SassRuntimePack
{
    /// <summary>Name of the MSBuild item that carries runtime packs.</summary>
    public const string ItemName = "SassRuntimePack";

    /// <summary>Metadata holding the .NET runtime identifier the pack provides (for example <c>osx-arm64</c>). Required.</summary>
    public const string RidMetadataName = "Rid";

    /// <summary>Metadata holding the directory that contains <c>&lt;rid&gt;/native/&lt;executable&gt;</c>. Required.</summary>
    public const string RuntimesPathMetadataName = "RuntimesPath";

    /// <summary>Metadata holding the Sass build variant. Informational only.</summary>
    public const string VariantMetadataName = "Variant";

    /// <summary>Metadata holding the selection priority. Higher wins, defaults to <c>0</c>.</summary>
    public const string PriorityMetadataName = "Priority";

    /// <summary>
    /// Initializes a new instance of the <see cref="SassRuntimePack"/> class.
    /// </summary>
    /// <param name="id">Identifier of the pack, normally the runtime package id.</param>
    /// <param name="rid">The .NET runtime identifier the pack provides.</param>
    /// <param name="runtimesPath">Directory containing <c>&lt;rid&gt;/native/&lt;executable&gt;</c>.</param>
    /// <param name="variant">Optional Sass build variant, used for diagnostics only.</param>
    /// <param name="priority">Selection priority. Higher wins when several packs provide the same RID.</param>
    /// <param name="source">Which contract the pack was declared through.</param>
    public SassRuntimePack(
        string id,
        string rid,
        string runtimesPath,
        string? variant = null,
        int priority = 0,
        SassRuntimePackSource source = SassRuntimePackSource.Item)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Pack id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(rid))
        {
            throw new ArgumentException("Pack RID must not be empty.", nameof(rid));
        }

        if (string.IsNullOrWhiteSpace(runtimesPath))
        {
            throw new ArgumentException("Pack runtimes path must not be empty.", nameof(runtimesPath));
        }

        Id = id.Trim();
        Rid = rid.Trim();
        RuntimesPath = runtimesPath.Trim();
        Variant = string.IsNullOrWhiteSpace(variant) ? null : variant!.Trim();
        Priority = priority;
        Source = source;
    }

    /// <summary>Identifier of the pack, normally the runtime package id.</summary>
    public string Id { get; }

    /// <summary>The .NET runtime identifier the pack provides.</summary>
    public string Rid { get; }

    /// <summary>Directory containing <c>&lt;rid&gt;/native/&lt;executable&gt;</c>.</summary>
    public string RuntimesPath { get; }

    /// <summary>Sass build variant, used for diagnostics only.</summary>
    public string? Variant { get; }

    /// <summary>Selection priority. Higher wins when several packs provide the same RID.</summary>
    public int Priority { get; }

    /// <summary>Which contract the pack was declared through.</summary>
    /// <remarks>
    /// After <see cref="Deduplicate"/>, a pack still marked <see cref="SassRuntimePackSource.LegacyProperty"/> is one
    /// that no runtime package described as an item - which is exactly the case worth reporting.
    /// </remarks>
    public SassRuntimePackSource Source { get; }

    /// <summary>
    /// Removes packs that resolve to the same RID and directory.
    /// </summary>
    /// <remarks>
    /// The same pack can legitimately reach the task twice - items are additive, and a package's props can be
    /// imported from more than one build folder - so duplicates are expected rather than exceptional.
    /// </remarks>
    /// <param name="packs">The packs to de-duplicate.</param>
    /// <returns>The distinct packs, first occurrence wins.</returns>
    public static IReadOnlyList<SassRuntimePack> Deduplicate(IEnumerable<SassRuntimePack> packs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SassRuntimePack>();

        foreach (var pack in packs)
        {
            if (seen.Add($"{pack.Rid}|{NormalizePath(pack.RuntimesPath)}"))
            {
                result.Add(pack);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a short description of the pack for build logs and error messages.
    /// </summary>
    /// <returns>For example <c>Scarlet.Sass.Runtime.darwin-arm64 (osx-arm64)</c>.</returns>
    public override string ToString()
    {
        return Variant is null
            ? $"{Id} ({Rid})"
            : $"{Id} ({Rid}, {Variant})";
    }

    /// <summary>
    /// Canonicalizes a directory for comparison purposes, tolerating paths the OS cannot resolve.
    /// </summary>
    /// <param name="path">The path to canonicalize.</param>
    /// <returns>The full path without a trailing separator, or the trimmed input if it cannot be canonicalized.</returns>
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            // An unresolvable path is still a usable key, it just cannot be compared structurally.
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
