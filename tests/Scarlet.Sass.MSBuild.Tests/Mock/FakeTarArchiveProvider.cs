using System.Text;
using Scarlet.Sass.Core.Providers;

namespace Scarlet.Sass.MSBuild.Tests.Mock;

/// <summary>
/// Fakes <see cref="ITarArchiveProvider"/> the same way <see cref="FakeZipArchiveProvider"/> fakes
/// <see cref="IZipArchiveProvider"/>: it hands back canned entries regardless of what stream it is given, so
/// tests can inject archive shapes (missing executables, path-escaping entries) without hand-rolling real tar
/// bytes for every scenario.
/// </summary>
public sealed class FakeTarArchiveProvider : ITarArchiveProvider
{
    private readonly IReadOnlyList<TarArchiveEntry> _entries;

    public FakeTarArchiveProvider()
        : this(new[] { ("dart-sass/sass", "fake Sass executable") })
    {
    }

    public FakeTarArchiveProvider(IEnumerable<string> entryNames)
        : this(entryNames.Select(name => (name, "fake Sass executable")))
    {
    }

    public FakeTarArchiveProvider(IEnumerable<(string Name, string Content)> entries)
    {
        _entries = entries
            .Select(e => new TarArchiveEntry(e.Name, isDirectory: false, Encoding.UTF8.GetBytes(e.Content)))
            .ToList();
    }

    public IEnumerable<TarArchiveEntry> ReadEntries(Stream tarStream) => _entries;
}
