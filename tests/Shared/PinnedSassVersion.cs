using System.Xml.Linq;

namespace Scarlet.Sass.Testing;

/// <summary>
/// The Dart Sass version the repository pins as <c>$(SassVersion)</c> in <c>Directory.Build.props</c>.
/// </summary>
/// <remarks>
/// Tests that download a real Dart Sass use this rather than a literal. The Dart Sass version bump workflow only rewrites
/// <c>Directory.Build.props</c>, so a literal silently falls behind with every release - and then the download
/// tests exercise a Dart Sass the packages no longer ship.
/// </remarks>
internal static class PinnedSassVersion
{
    private static readonly Lazy<string> LazyValue = new(Read);

    public static string Value => LazyValue.Value;

    private static string Read()
    {
        var propsPath = System.IO.Path.Combine(RepositoryRoot.Path, "Directory.Build.props");
        var versions = XDocument.Load(propsPath).Descendants("SassVersion").ToList();

        if (versions.Count != 1 || string.IsNullOrWhiteSpace(versions[0].Value))
        {
            throw new InvalidOperationException(
                $"Expected exactly one non-empty <SassVersion> in '{propsPath}', found {versions.Count}.");
        }

        return versions[0].Value.Trim();
    }
}
