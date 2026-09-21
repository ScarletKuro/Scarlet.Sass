using System.IO.Abstractions;
using System.IO.Compression;

namespace Scarlet.Sass.MSBuild.Tests.Mock;

public sealed class FakeZipArchiveProvider : IZipArchiveProvider
{
    private readonly byte[] _zipBytes;
    private readonly IFileSystem _fileSystem;

    public FakeZipArchiveProvider(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "Sass", "Sass.exe" })
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write("fake Sass executable");
            }
        }

        _zipBytes = ms.ToArray();
    }

    public ZipArchive OpenRead(string archiveFileName)
    {
        return new ZipArchive(new MemoryStream(_zipBytes), ZipArchiveMode.Read);
    }

    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
    {
        if (!overwrite && _fileSystem.File.Exists(destinationFileName))
        {
            throw new IOException($"The file '{destinationFileName}' already exists.");
        }

        using var entryStream = source.Open();
        using var fileStream = _fileSystem.File.Create(destinationFileName);
        entryStream.CopyTo(fileStream);
    }
}
