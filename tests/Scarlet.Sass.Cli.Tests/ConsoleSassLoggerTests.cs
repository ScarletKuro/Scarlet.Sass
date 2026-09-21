namespace Scarlet.Sass.Cli.Tests;

public class ConsoleSassLoggerTests
{
    [Fact]
    public void LogMessage_ShouldPrefixAndWriteToTheGivenWriter()
    {
        // Arrange - stderr, never stdout: `dotnet Sass ... | jq` must not receive the tool's own chatter
        var stderr = new StringWriter();
        var logger = new ConsoleSassLogger(stderr);

        // Act
        logger.LogMessage("Another process is downloading the Sass runtime. Waiting...");

        // Assert
        Assert.Equal(
            "Scarlet.Sass: Another process is downloading the Sass runtime. Waiting..." + Environment.NewLine,
            stderr.ToString());
    }
}
