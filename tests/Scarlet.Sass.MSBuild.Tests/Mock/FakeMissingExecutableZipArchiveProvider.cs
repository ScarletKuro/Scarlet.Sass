using System.IO.Compression;

namespace Scarlet.Sass.MSBuild.Tests.Mock;

public sealed class FakeMissingExecutableZipArchiveProvider : IZipArchiveProvider
{
    public ZipArchive OpenRead(string archiveFileName)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Sass-linux-x64/not-Sass");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not Sass");
        }

        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
    {
        source.ExtractToFile(destinationFileName, overwrite);
    }
}