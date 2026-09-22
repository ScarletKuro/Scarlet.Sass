using System.Text.Json;
using Scarlet.Sass.Cli.Tests.Mock;

namespace Scarlet.Sass.Cli.Tests;

/// <summary>
/// Covers what <c>--scarlet-info</c> prints for each way Sass can be resolved.
/// </summary>
/// <remarks>
/// This is the output people paste into issues, and <c>tests/e2e/cli-tool/verify.sh</c> greps it to prove
/// nothing was downloaded, so the wording is a contract rather than decoration.
/// </remarks>
public class DiagnosticsReportTests
{
    // SassSource is internal, so the source is named rather than passed: an internal type cannot appear in
    // the signature of a public xunit test method.
    [Theory]
    [InlineData(nameof(SassSource.Embedded), "embedded")]
    [InlineData(nameof(SassSource.Cache), "cache")]
    [InlineData(nameof(SassSource.Downloaded), "downloaded")]
    [InlineData(nameof(SassSource.Explicit), "explicit")]
    [InlineData(nameof(SassSource.NotFound), "not found")]
    public void ToText_ShouldDescribeEverySource(string sourceName, string expected)
    {
        // Arrange
        var resolution = CreateResolution(Enum.Parse<SassSource>(sourceName));

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains($"Source", report);
        Assert.Contains(expected, report);
    }

    [Fact]
    public void ToText_WhenResolved_ShouldNotOfferADownloadUrl()
    {
        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(SassSource.Embedded), CreateOptions());

        // Assert
        Assert.Contains("(not needed)", report);
        Assert.DoesNotContain("https://github.com/sass/dart-sass/releases/download", report);
    }

    [Fact]
    public void ToText_WhenNotResolved_ShouldNameTheExactArchiveItWouldFetch()
    {
        // Arrange
        var resolution = CreateResolution(SassSource.NotFound, executablePath: null, version: "1.4.2");

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains("https://github.com/sass/dart-sass/releases/download/1.4.2/dart-sass-1.4.2-linux-x64.tar.gz", report);
    }

    [Fact]
    public void ToText_WhenLatestIsRequested_ShouldPointAtTheLatestRelease()
    {
        // Arrange
        var resolution = CreateResolution(SassSource.NotFound, executablePath: null, version: SassCliOptions.LatestVersion);

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains("https://github.com/sass/dart-sass/releases/latest", report);
    }

    [Fact]
    public void ToText_ShouldReportWhichEnvironmentVariablesAreSet()
    {
        // Arrange - the pinned version is 1.4.2, so setting it here proves the line echoes the override
        // rather than falling back to the pin
        var options = CreateOptions(new Dictionary<string, string>
        {
            [SassCliOptions.VersionVariable] = "1.2.3",
            [SassCliOptions.CacheVariable] = "/cache",
            [SassCliOptions.NoEmbeddedVariable] = "1",
            [SassCliOptions.PassthroughVariable] = "1",
            [SassCliOptions.DiagnosticsVariable] = "1",
            [SassCliOptions.DownloadTimeoutVariable] = "42"
        });

        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(SassSource.Embedded), options);

        // Assert
        Assert.Contains($"{SassCliOptions.VersionVariable,-28}1.2.3", report);
        Assert.Contains($"{SassCliOptions.NoEmbeddedVariable,-28}enabled", report);
        Assert.Contains($"{SassCliOptions.PassthroughVariable,-28}enabled", report);
        Assert.Contains($"{SassCliOptions.DiagnosticsVariable,-28}enabled", report);
        Assert.Contains($"{SassCliOptions.CacheVariable,-28}/cache", report);
        Assert.Contains($"{SassCliOptions.DownloadTimeoutVariable,-28}42", report);
    }

    [Fact]
    public void ToText_WithNothingSet_ShouldSayUnset()
    {
        // Arrange - a genuinely empty environment, rather than CreateOptions()'s default, which pins
        // SCARLET_Sass_CACHE so unrelated tests don't depend on the host's filesystem layout
        var options = SassCliOptions.FromEnvironment(new FakeEnvironmentProvider(), "1.4.2");

        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(SassSource.Embedded), options);

        // Assert
        Assert.Contains($"{SassCliOptions.NoEmbeddedVariable,-28}(unset)", report);
        Assert.Contains($"{SassCliOptions.PathVariable,-28}(unset)", report);
        Assert.Contains($"{SassCliOptions.VersionVariable,-28}(unset)", report);
        Assert.Contains($"{SassCliOptions.CacheVariable,-28}(unset)", report);
        Assert.Contains($"{SassCliOptions.DownloadTimeoutVariable,-28}(unset)", report);
    }

    [Theory]
    [InlineData("", "Scarlet.Sass.Cli (portable)")]
    [InlineData("win-x64", "Scarlet.Sass.Cli.win-x64")]
    [InlineData("linux-arm64", "Scarlet.Sass.Cli.linux-arm64")]
    public void DescribePackage_ShouldNameThePackageThisBuildCameFrom(string rid, string expected)
    {
        // Act & Assert - the portable package reports itself differently, and only one of the two can ever
        // be reached from a test build, which is why the identifier is a parameter
        Assert.Equal(expected, DiagnosticsReport.DescribePackage(rid));
    }

    [Fact]
    public void ToJson_ShouldEmitTheSameFactsAsParseableJson()
    {
        // Arrange
        var options = CreateOptions(new Dictionary<string, string>
        {
            [SassCliOptions.VersionVariable] = "1.2.3",
            [SassCliOptions.CacheVariable] = "/cache",
            [SassCliOptions.NoEmbeddedVariable] = "1",
            [SassCliOptions.PassthroughVariable] = "1",
            [SassCliOptions.DiagnosticsVariable] = "1",
            [SassCliOptions.DownloadTimeoutVariable] = "42"
        });

        // Act
        var json = DiagnosticsReport.ToJson(CreateResolution(SassSource.Embedded), options);

        // Assert
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("embedded", root.GetProperty("source").GetString());
        Assert.Equal("linux-x64", root.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal("/tool/dart-sass/sass", root.GetProperty("SassExecutable").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("downloadUrl").ValueKind);

        var environment = root.GetProperty("environment");
        Assert.Equal("1.2.3", environment.GetProperty(SassCliOptions.VersionVariable).GetString());
        Assert.Equal("/cache", environment.GetProperty(SassCliOptions.CacheVariable).GetString());
        Assert.True(environment.GetProperty(SassCliOptions.NoEmbeddedVariable).GetBoolean());
        Assert.True(environment.GetProperty(SassCliOptions.PassthroughVariable).GetBoolean());
        Assert.True(environment.GetProperty(SassCliOptions.DiagnosticsVariable).GetBoolean());
        Assert.Equal("42", environment.GetProperty(SassCliOptions.DownloadTimeoutVariable).GetString());
    }

    [Fact]
    public void ToJson_WhenNotResolved_ShouldCarryTheDownloadUrlAndFailureReason()
    {
        // Arrange
        var resolution = CreateResolution(SassSource.NotFound, executablePath: null, failureReason: "nothing yet");

        // Act
        var json = DiagnosticsReport.ToJson(resolution, CreateOptions());

        // Assert
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("SassExecutable").ValueKind);
        Assert.Contains("dart-sass-1.4.2", root.GetProperty("downloadUrl").GetString()!);
        Assert.Equal("nothing yet", root.GetProperty("failureReason").GetString());
    }

    [Fact]
    public void ToJson_WithNothingSet_ShouldNullTheStringOverridesRatherThanTheResolvedDefault()
    {
        // Arrange - a genuinely empty environment, rather than CreateOptions()'s default, which pins
        // SCARLET_Sass_CACHE so unrelated tests don't depend on the host's filesystem layout
        var options = SassCliOptions.FromEnvironment(new FakeEnvironmentProvider(), "1.4.2");

        // Act
        var json = DiagnosticsReport.ToJson(CreateResolution(SassSource.Embedded), options);

        // Assert
        using var document = JsonDocument.Parse(json);
        var environment = document.RootElement.GetProperty("environment");

        Assert.Equal(JsonValueKind.Null, environment.GetProperty(SassCliOptions.PathVariable).ValueKind);
        Assert.Equal(JsonValueKind.Null, environment.GetProperty(SassCliOptions.VersionVariable).ValueKind);
        Assert.Equal(JsonValueKind.Null, environment.GetProperty(SassCliOptions.CacheVariable).ValueKind);
        Assert.Equal(JsonValueKind.Null, environment.GetProperty(SassCliOptions.DownloadTimeoutVariable).ValueKind);
        Assert.False(environment.GetProperty(SassCliOptions.NoEmbeddedVariable).GetBoolean());
        Assert.False(environment.GetProperty(SassCliOptions.PassthroughVariable).GetBoolean());
        Assert.False(environment.GetProperty(SassCliOptions.DiagnosticsVariable).GetBoolean());
    }

    private static SassResolution CreateResolution(
        SassSource source,
        string? executablePath = "/tool/dart-sass/sass",
        string version = "1.4.2",
        string? failureReason = null)
    {
        return new SassResolution(
            executablePath,
            source,
            Platform.LinuxX64,
            "linux-x64",
            version,
            "/cache",
            $"/cache/runtimes/{version}",
            "/tool/dart-sass/sass",
            failureReason);
    }

    private static SassCliOptions CreateOptions(IDictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string> { [SassCliOptions.CacheVariable] = "/cache" };

        return SassCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), "1.4.2");
    }
}
