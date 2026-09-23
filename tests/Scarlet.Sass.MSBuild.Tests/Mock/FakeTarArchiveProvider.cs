namespace Scarlet.Sass.MSBuild.Tests.Mock;

/// <summary>
/// Fakes <see cref="ITarArchiveProvider"/> the same way <see cref="FakeZipArchiveProvider"/> fakes
/// <see cref="IZipArchiveProvider"/>: it hands back canned entries regardless of what stream it is given, so
/// tests can inject archive shapes (missing launchers, path-escaping entries) without hand-rolling real tar
/// bytes for every scenario.
/// </summary>
public sealed class FakeTarArchiveProvider : ITarArchiveProvider
{
    private readonly IReadOnlyList<FakeTarEntry> _entries;

    public FakeTarArchiveProvider(IReadOnlyList<FakeTarEntry> entries)
    {
        _entries = entries;
    }

    public void ReadEntries(Stream tarStream, Action<string, bool, Stream> readEntry)
    {
        foreach (var entry in _entries)
        {
            using var stream = new MemoryStream(entry.Content);
            readEntry(entry.Name, entry.IsDirectory, stream);
        }
    }
}
