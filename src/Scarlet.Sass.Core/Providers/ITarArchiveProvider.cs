using System;
using System.IO;

namespace Scarlet.Sass.Core.Providers;

/// <summary>
/// Abstracts reading entries out of an uncompressed tar stream so archive parsing can be replaced in tests,
/// the same way <see cref="IZipArchiveProvider"/> lets zip extraction be replaced.
/// </summary>
public interface ITarArchiveProvider
{
    /// <summary>
    /// Reads every supported entry from <paramref name="tarStream"/> in archive order. The stream is expected
    /// to already be decompressed (for example, the output of a <see cref="System.IO.Compression.GZipStream"/>).
    /// </summary>
    void ReadEntries(Stream tarStream, Action<string, bool, Stream> readEntry);
}
