using System.Collections.Generic;
using System.IO;

namespace Scarlet.Sass.Core.Providers;

/// <summary>
/// Abstracts reading entries out of an uncompressed tar stream so archive parsing can be replaced in tests,
/// the same way <see cref="IZipArchiveProvider"/> lets zip extraction be replaced.
/// </summary>
public interface ITarArchiveProvider
{
    /// <summary>
    /// Reads every entry from <paramref name="tarStream"/> in archive order. The stream is expected to
    /// already be decompressed (for example, the output of a <see cref="System.IO.Compression.GZipStream"/>).
    /// </summary>
    IEnumerable<TarArchiveEntry> ReadEntries(Stream tarStream);
}

/// <summary>
/// A single file or directory entry extracted from a tar archive.
/// </summary>
public sealed class TarArchiveEntry
{
    public TarArchiveEntry(string name, bool isDirectory, byte[] content)
    {
        Name = name;
        IsDirectory = isDirectory;
        Content = content;
    }

    /// <summary>
    /// The entry's path as recorded in the archive, using <c>/</c> as the separator.
    /// </summary>
    public string Name { get; }

    public bool IsDirectory { get; }

    /// <summary>
    /// The entry's raw file content. Empty for directory entries.
    /// </summary>
    public byte[] Content { get; }
}
