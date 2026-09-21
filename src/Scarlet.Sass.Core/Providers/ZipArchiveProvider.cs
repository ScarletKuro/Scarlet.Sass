using System.IO.Compression;

namespace Scarlet.Sass.Core.Providers;

/// <summary>
/// Default <see cref="IZipArchiveProvider"/> implementation that delegates to <see cref="ZipFile"/>.
/// </summary>
public sealed class ZipArchiveProvider : IZipArchiveProvider
{
    /// <inheritdoc />
    public ZipArchive OpenRead(string path) => ZipFile.OpenRead(path);

    /// <inheritdoc />
    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite) => source.ExtractToFile(destinationFileName, overwrite);

    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static ZipArchiveProvider Instance { get; } = new();
}