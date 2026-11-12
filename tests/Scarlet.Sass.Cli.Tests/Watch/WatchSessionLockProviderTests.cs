using Scarlet.Sass.Cli.Watch;

namespace Scarlet.Sass.Cli.Tests.Watch;

public sealed class WatchSessionLockProviderTests : IDisposable
{
    private readonly string _cacheRoot =
        Path.Combine(Path.GetTempPath(), "scarlet-sass-lock-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void TryAcquire_WhenAnOutputIsAlreadyLocked_ShouldRejectTheOverlappingSession()
    {
        var provider = new WatchSessionLockProvider(_cacheRoot);

        var acquiredFirst = provider.TryAcquire([CreateInvocation("app.css")], out var first);
        using var firstLease = first;
        var acquiredOverlapping = provider.TryAcquire([CreateInvocation("app.css")], out var overlapping);

        Assert.True(acquiredFirst);
        Assert.NotNull(firstLease);
        Assert.False(acquiredOverlapping);
        Assert.Null(overlapping);
    }

    [Fact]
    public void TryAcquire_WhenLockFileAlreadyExistsButIsUnlocked_ShouldAllowAnotherSession()
    {
        var provider = new WatchSessionLockProvider(_cacheRoot);
        var invocation = CreateInvocation("app.css");

        Assert.True(provider.TryAcquire([invocation], out var first));
        first.Dispose();
        Assert.Single(Directory.GetFiles(Path.Combine(_cacheRoot, "watch"), "*.lock"));

        var acquiredNext = provider.TryAcquire([invocation], out var next);
        using var nextLease = next;
        Assert.True(acquiredNext);
    }

    [Fact]
    public void TryAcquire_WhenProjectsUseTheSameRelativeOutput_ShouldAllowBothSessions()
    {
        var provider = new WatchSessionLockProvider(_cacheRoot);
        var output = Path.Combine("wwwroot", "css", "app.css");

        var acquiredFirst = provider.TryAcquire(
            [CreateInvocation(Path.Combine(Path.GetTempPath(), "project-a"), output)], out var first);
        using var firstLease = first;
        var acquiredSecond = provider.TryAcquire(
            [CreateInvocation(Path.Combine(Path.GetTempPath(), "project-b"), output)], out var second);
        using var secondLease = second;

        Assert.True(acquiredFirst);
        Assert.True(acquiredSecond);
    }

    /// <summary>
    /// A directory input that has never been compiled reports no generated files, so the output directory itself
    /// is what keeps a second watcher from compiling into it.
    /// </summary>
    [Fact]
    public void TryAcquire_WhenADirectoryOutputHasNoGeneratedFilesYet_ShouldStillRejectAnOverlappingSession()
    {
        var provider = new WatchSessionLockProvider(_cacheRoot);
        var invocation = CreateInvocation("app.css") with
        {
            OutputPaths = [Path.Combine(Path.GetTempPath(), "app", "wwwroot", "css")],
            GeneratedPaths = []
        };

        var acquiredFirst = provider.TryAcquire([invocation], out var first);
        using var firstLease = first;
        var acquiredOverlapping = provider.TryAcquire([invocation], out var overlapping);

        Assert.True(acquiredFirst);
        Assert.False(acquiredOverlapping);
        Assert.Null(overlapping);
    }

    [Fact]
    public void TryAcquire_WhenOnlyAnAdditionalGeneratedFileOverlaps_ShouldRejectTheSession()
    {
        var provider = new WatchSessionLockProvider(_cacheRoot);
        var sharedMap = Path.Combine(Path.GetTempPath(), "app", "wwwroot", "css", "shared.css.map");
        var first = CreateInvocation("first.css");
        var second = CreateInvocation("second.css");
        first = first with { GeneratedPaths = [first.OutputPaths[0], sharedMap] };
        second = second with { GeneratedPaths = [second.OutputPaths[0], sharedMap] };

        var acquiredFirst = provider.TryAcquire([first], out var firstLock);
        using var firstLease = firstLock;
        var acquiredOverlapping = provider.TryAcquire([second], out var overlappingLock);

        Assert.True(acquiredFirst);
        Assert.False(acquiredOverlapping);
        Assert.Null(overlappingLock);
    }

    [Fact]
    public void TryAcquire_WhenPathNormalizationFails_ShouldWrapTheFailureAndLeaveNoLease()
    {
        var provider = new WatchSessionLockProvider(_cacheRoot);
        var invocation = CreateInvocation("app.css") with
        {
            WorkingDirectory = "relative",
            OutputPaths = ["app.css"],
            GeneratedPaths = ["app.css"]
        };
        using var staleLease = new MemoryStream();
        IDisposable? lease = staleLease;

        var exception = Assert.Throws<SassWatchException>(() => provider.TryAcquire([invocation], out lease));

        Assert.Contains("could not create watch-session locks", exception.Message);
        Assert.Null(lease);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizePath_ShouldApplyTheRequestedPlatformCasing(bool isWindows)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "MixedCaseProject");
        var relativePath = Path.Combine("Styles", "App.css");
        var fullPath = Path.GetFullPath(relativePath, workingDirectory);

        var normalized = WatchSessionLockProvider.NormalizePath(relativePath, workingDirectory, isWindows);

        Assert.Equal(isWindows ? fullPath.ToUpperInvariant() : fullPath, normalized);
    }

    private static SassWatchInvocation CreateInvocation(string outputName)
    {
        return CreateInvocation(Path.Combine(Path.GetTempPath(), "app"), Path.Combine("wwwroot", "css", outputName));
    }

    private static SassWatchInvocation CreateInvocation(string projectDirectory, string outputPath)
    {
        outputPath = Path.GetFullPath(outputPath, projectDirectory);

        return new SassWatchInvocation(
            "dart",
            "sass.snapshot --watch",
            projectDirectory,
            "sass",
            [Path.Combine(projectDirectory, "Styles", "app.scss")],
            [outputPath],
            [outputPath]);
    }
}
