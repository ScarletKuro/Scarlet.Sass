using System.IO.Compression;

namespace Scarlet.Sass.Core.Providers;

/// <summary>
/// Abstracts zip archive operations so they can be replaced in tests.
/// </summary>
public interface IZipArchiveProvider
{
    /// <inheritdoc cref="ZipFile.OpenRead"/>
    ZipArchive OpenRead(string archiveFileName);

    /// <inheritdoc cref="ZipFileExtensions.ExtractToFile(ZipArchiveEntry,string,bool)"/>
    void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite);
}