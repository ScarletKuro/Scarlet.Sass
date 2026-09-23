using System.Text;
using Scarlet.Sass.Core.Providers;

namespace Scarlet.Sass.MSBuild.Tests;

public class TarArchiveProviderTests
{
    [Fact]
    public void ReadEntries_WithDirectoryEntry_ShouldReportDirectoryAndContinue()
    {
        using var tar = CreateTar(
            new TarFixtureEntry("dart-sass/", string.Empty, '5'),
            new TarFixtureEntry("dart-sass/sass", "launcher", '0'));

        var entries = ReadEntries(tar);

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal("dart-sass/", entry.Name);
                Assert.True(entry.IsDirectory);
                Assert.Equal(string.Empty, entry.Content);
            },
            entry =>
            {
                Assert.Equal("dart-sass/sass", entry.Name);
                Assert.False(entry.IsDirectory);
                Assert.Equal("launcher", entry.Content);
            });
    }

    [Fact]
    public void ReadEntries_WithOldNormalFileType_ShouldReadFile()
    {
        using var tar = CreateTar(new TarFixtureEntry("dart-sass/sass", "launcher", '\0'));

        var entry = Assert.Single(ReadEntries(tar));

        Assert.Equal("dart-sass/sass", entry.Name);
        Assert.False(entry.IsDirectory);
        Assert.Equal("launcher", entry.Content);
    }

    [Fact]
    public void ReadEntries_WithSymlinkEntry_ShouldSkipEntry()
    {
        using var tar = CreateTar(new TarFixtureEntry("dart-sass/sass", string.Empty, '2'));

        Assert.Empty(ReadEntries(tar));
    }

    private static List<ReadTarEntry> ReadEntries(Stream tar)
    {
        var entries = new List<ReadTarEntry>();
        TarArchiveProvider.Instance.ReadEntries(tar, (name, isDirectory, stream) =>
        {
            using var content = new MemoryStream();
            stream.CopyTo(content);
            entries.Add(new ReadTarEntry(name, isDirectory, Encoding.UTF8.GetString(content.ToArray())));
        });

        return entries;
    }

    private static MemoryStream CreateTar(params TarFixtureEntry[] entries)
    {
        var tar = new MemoryStream();

        foreach (var entry in entries)
        {
            WriteTarEntry(tar, entry);
        }

        tar.Write(new byte[1024], 0, 1024);
        tar.Position = 0;
        return tar;
    }

    private static void WriteTarEntry(Stream stream, TarFixtureEntry entry)
    {
        var content = Encoding.UTF8.GetBytes(entry.Content);
        var header = new byte[512];

        WriteAscii(header, 0, 100, entry.Name);
        WriteAscii(header, 100, 8, "0000777\0");
        WriteAscii(header, 108, 8, "0000000\0");
        WriteAscii(header, 116, 8, "0000000\0");
        WriteAscii(header, 124, 12, Convert.ToString(content.Length, 8).PadLeft(11, '0') + "\0");
        WriteAscii(header, 136, 12, "00000000000\0");
        header[156] = (byte)entry.TypeFlag;
        WriteAscii(header, 257, 6, "ustar\0");
        WriteAscii(header, 263, 2, "00");

        for (var i = 148; i < 156; i++)
        {
            header[i] = (byte)' ';
        }

        WriteAscii(header, 148, 8, Convert.ToString(header.Sum(b => b), 8).PadLeft(6, '0') + "\0 ");

        stream.Write(header, 0, header.Length);
        stream.Write(content, 0, content.Length);

        var padding = (512 - (content.Length % 512)) % 512;
        if (padding > 0)
        {
            stream.Write(new byte[padding], 0, padding);
        }
    }

    private static void WriteAscii(byte[] buffer, int offset, int length, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        Array.Copy(bytes, 0, buffer, offset, Math.Min(bytes.Length, length));
    }

    private sealed record TarFixtureEntry(string Name, string Content, char TypeFlag);

    private sealed record ReadTarEntry(string Name, bool IsDirectory, string Content);
}
