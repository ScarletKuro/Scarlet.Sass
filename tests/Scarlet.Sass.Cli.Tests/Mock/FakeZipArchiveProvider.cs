using System.IO.Abstractions;
using System.IO.Compression;

namespace Scarlet.Sass.Cli.Tests.Mock;

/// <summary>
/// An in-memory Sass archive, so the download path can be exercised without a network or a real zip.
/// </summary>
internal sealed class FakeZipArchiveProvider : IZipArchiveProvider
{
    private readonly byte[] _zipBytes;
    private readonly IFileSystem _fileSystem;

    public FakeZipArchiveProvider(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Real Sass archives nest the executable inside a directory; mirror that so the downloader's
            // entry search is exercised rather than short-circuited.
            foreach (var name in new[] { "Sass-linux-x64/Sass", "Sass-windows-x64/Sass.exe" })
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write("fake Sass executable");
            }
        }

        _zipBytes = buffer.ToArray();
    }

    public ZipArchive OpenRead(string archiveFileName) => new(new MemoryStream(_zipBytes), ZipArchiveMode.Read);

    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
    {
        using var entryStream = source.Open();
        using var fileStream = _fileSystem.File.Create(destinationFileName);
        entryStream.CopyTo(fileStream);
    }
}
