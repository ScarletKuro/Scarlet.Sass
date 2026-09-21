namespace Scarlet.Sass.Core;

public sealed class PlatformInfo
{
    public string Rid { get; }
    public string DirectoryName { get; }
    public string DownloadName { get; }
    public string PackageName { get; }
    public string ExecutableName { get; }

    public PlatformInfo(
        string rid,
        string directoryName,
        string downloadName,
        string packageName,
        string executableName)
    {
        Rid = rid;
        DirectoryName = directoryName;
        DownloadName = downloadName;
        PackageName = packageName;
        ExecutableName = executableName;
    }
}
