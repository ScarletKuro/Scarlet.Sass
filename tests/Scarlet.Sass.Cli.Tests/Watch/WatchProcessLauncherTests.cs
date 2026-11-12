using Scarlet.Sass.Cli.Watch;

namespace Scarlet.Sass.Cli.Tests.Watch;

public class WatchProcessLauncherTests
{
    [Fact]
    public void Run_WithNoInvocations_ShouldSucceedWithoutStartingAnything()
    {
        var launcher = new WatchProcessLauncher(new ConsoleSassLogger(new StringWriter()));

        Assert.Equal(ExitCodes.Success, launcher.Run([]));
    }

    [Fact]
    public void CreateStartInfo_ShouldPassTheBuildCommandLineVerbatimFromTheBuildWorkingDirectory()
    {
        var invocation = new SassWatchInvocation(
            "/sass/src/dart",
            "\"/sass/src/sass.snapshot\" --update \"/repo project/Styles/app.scss:/repo project/wwwroot/css/app.css\" --watch",
            "/repo project",
            "/sass/sass",
            ["/repo project/Styles/app.scss"],
            ["/repo project/wwwroot/css/app.css"],
            ["/repo project/wwwroot/css/app.css"]);

        var startInfo = WatchProcessLauncher.CreateStartInfo(invocation);

        Assert.Equal("/sass/src/dart", startInfo.FileName);
        Assert.Equal(invocation.ProcessArguments, startInfo.Arguments);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal("/repo project", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);
    }
}
