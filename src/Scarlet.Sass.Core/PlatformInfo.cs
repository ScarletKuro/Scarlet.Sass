namespace Scarlet.Sass.Core;

public sealed class PlatformInfo
{
    public string Rid { get; }
    public string DirectoryName { get; }
    public string DownloadName { get; }
    public string PackageName { get; }
    public string LauncherName { get; }
    public string DartExecutableName { get; }
    public string ArchiveExtension { get; }

    public PlatformInfo(
        string rid,
        string directoryName,
        string downloadName,
        string packageName,
        string launcherName,
        string dartExecutableName,
        string archiveExtension)
    {
        Rid = rid;
        DirectoryName = directoryName;
        DownloadName = downloadName;
        PackageName = packageName;
        LauncherName = launcherName;
        DartExecutableName = dartExecutableName;
        ArchiveExtension = archiveExtension;
    }
}
