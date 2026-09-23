using System;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Tar;

namespace Scarlet.Sass.Core.Providers;

/// <summary>
/// Default <see cref="ITarArchiveProvider"/> implementation. Dart Sass's non-Windows releases ship as
/// tar.gz, and no tar reader ships in netstandard2.0, so this uses SharpZipLib for the tar stream while
/// leaving destination path validation and filesystem writes to <see cref="SassDownloader"/>.
/// </summary>
public sealed class TarArchiveProvider : ITarArchiveProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static TarArchiveProvider Instance { get; } = new();

    /// <inheritdoc />
    public void ReadEntries(Stream tarStream, Action<string, bool, Stream> readEntry)
    {
        using var archive = new TarInputStream(tarStream, Encoding.UTF8);
        archive.IsStreamOwner = false;

        while (archive.GetNextEntry() is { } entry)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            if (entry.IsDirectory)
            {
                readEntry(entry.Name, true, Stream.Null);
                continue;
            }

            if (IsRegularFile(entry))
            {
                readEntry(entry.Name, false, archive);
            }
        }
    }

    private static bool IsRegularFile(TarEntry entry)
    {
        var typeFlag = entry.TarHeader.TypeFlag;
        return typeFlag is TarHeader.LF_NORMAL or TarHeader.LF_OLDNORM;
    }
}
