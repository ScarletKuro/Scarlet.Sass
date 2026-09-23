using System.IO.Compression;

namespace Scarlet.Sass.MSBuild.Tests.Mock;

public sealed class FakeMissingExecutableZipArchiveProvider : IZipArchiveProvider
{
    public ZipArchive OpenRead(string archiveFileName)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("dart-sass/not-sass");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not Sass");
        }

        stream.Position = 0;

        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
    {
        source.ExtractToFile(destinationFileName, overwrite);
    }
}
