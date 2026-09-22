using System.IO.Abstractions;
using System.IO.Compression;

namespace Scarlet.Sass.Cli.Tests.Mock;

/// <summary>
/// An in-memory Sass archive, so the download path can be exercised without a network or a real zip.
/// </summary>
/// <remarks>
/// The real <c>SassDownloader</c> only reaches <see cref="IZipArchiveProvider"/> for Windows platforms;
/// every other platform goes through a hand-rolled tar/gzip reader instead. The entry layout mirrors the
/// real <c>dart-sass-{version}-windows-{arch}.zip</c> release assets: a single top-level <c>dart-sass</c>
/// directory containing the launcher script.
/// </remarks>
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
            var entry = archive.CreateEntry("dart-sass/sass.bat");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("fake Sass executable");
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

