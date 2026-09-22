using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Scarlet.Sass.Core.Providers;

/// <summary>
/// Default <see cref="ITarArchiveProvider"/> implementation. Dart Sass's non-Windows releases ship as
/// tar.gz, and no tar reader ships in netstandard2.0, so this hand-rolls the USTAR format: a sequence of
/// 512-byte headers each followed by the entry's content padded up to the next 512-byte boundary, ending
/// with an all-zero header.
/// </summary>
public sealed class TarArchiveProvider : ITarArchiveProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static TarArchiveProvider Instance { get; } = new();

    /// <inheritdoc />
    public IEnumerable<TarArchiveEntry> ReadEntries(Stream tarStream)
    {
        var header = new byte[512];

        while (true)
        {
            ReadExactly(tarStream, header, 0, header.Length);
            if (IsAllZero(header))
            {
                yield break;
            }

            var name = ReadNullTerminatedAscii(header, 0, 100);
            var sizeText = ReadNullTerminatedAscii(header, 124, 12).Trim();
            var typeFlag = (char)header[156];
            var size = string.IsNullOrWhiteSpace(sizeText) ? 0 : Convert.ToInt64(sizeText, 8);

            if (string.IsNullOrEmpty(name))
            {
                SkipEntry(tarStream, size);
                continue;
            }

            if (typeFlag == '5')
            {
                yield return new TarArchiveEntry(name, isDirectory: true, Array.Empty<byte>());
                continue;
            }

            var content = ReadEntryContent(tarStream, size);
            SkipPadding(tarStream, size);

            yield return new TarArchiveEntry(name, isDirectory: false, content);
        }
    }

    private static byte[] ReadEntryContent(Stream stream, long size)
    {
        var content = new byte[size];
        ReadExactly(stream, content, 0, content.Length);
        return content;
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, offset + read, count - read);
            if (n == 0)
            {
                throw new EndOfStreamException("Unexpected end of tar archive.");
            }

            read += n;
        }
    }

    private static bool IsAllZero(byte[] buffer)
    {
        foreach (var value in buffer)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadNullTerminatedAscii(byte[] buffer, int offset, int count)
    {
        var length = 0;
        while (length < count && buffer[offset + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(buffer, offset, length);
    }

    private static void SkipEntry(Stream stream, long size)
    {
        var buffer = new byte[81920];
        var remaining = size;
        while (remaining > 0)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of tar archive entry.");
            }

            remaining -= read;
        }

        SkipPadding(stream, size);
    }

    private static void SkipPadding(Stream stream, long size)
    {
        var padding = (512 - (size % 512)) % 512;
        while (padding > 0)
        {
            if (stream.ReadByte() < 0)
            {
                throw new EndOfStreamException("Unexpected end of tar archive padding.");
            }

            padding--;
        }
    }
}
