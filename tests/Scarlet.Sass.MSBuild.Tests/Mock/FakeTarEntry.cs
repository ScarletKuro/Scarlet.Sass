using System.Text;

namespace Scarlet.Sass.MSBuild.Tests.Mock;

public sealed class FakeTarEntry
{
    private FakeTarEntry(string name, bool isDirectory, byte[] content)
    {
        Name = name;
        IsDirectory = isDirectory;
        Content = content;
    }

    public string Name { get; }

    public bool IsDirectory { get; }

    public byte[] Content { get; }

    public static FakeTarEntry File(string name, string content) =>
        new(name, isDirectory: false, Encoding.UTF8.GetBytes(content));

    public static FakeTarEntry Directory(string name) =>
        new(name, isDirectory: true, Array.Empty<byte>());
}