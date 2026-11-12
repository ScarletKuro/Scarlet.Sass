using System.Text;
using System.Text.Json;
using Scarlet.Sass.Cli.Watch;

namespace Scarlet.Sass.Cli.Tests.Watch;

public class MsBuildWatchConfigurationProviderTests
{
    [Fact]
    public void ParseTargetResult_WithAnEmptySuccessfulTarget_ShouldReturnNoInvocations()
    {
        const string json =
            """
            {
              "TargetResults": {
                "ResolveSassWatchConfiguration": {
                  "Result": "Success"
                }
              }
            }
            """;

        var result = MsBuildWatchConfigurationProvider.ParseTargetResult(json, "App.csproj");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseTargetResult_ShouldDecodeTheCommandLineAndEveryPathLosslessly()
    {
        const string arguments =
            "\"C:\\sass\\src\\sass.snapshot\" --update --style=compressed "
            + "\"C:\\repo with spaces\\Styles\\app.scss:C:\\repo with spaces\\wwwroot\\css\\app.css\" "
            + "\"C:\\repo with spaces\\Styles\\ä;b.scss:C:\\repo with spaces\\wwwroot\\css\\ä;b.css\" --watch";
        var inputs = new[] { "C:\\repo with spaces\\Styles\\app.scss", "C:\\repo with spaces\\Styles\\ä;b.scss" };
        var outputs = new[] { "C:\\repo with spaces\\wwwroot\\css\\app.css", "C:\\repo with spaces\\wwwroot\\css\\ä;b.css" };
        var json = CreateJson(new Dictionary<string, string>
        {
            ["Identity"] = inputs[0],
            ["ProtocolVersion"] = "1",
            ["ExecutablePath"] = "C:\\sass\\src\\dart.exe",
            ["DisplayPath"] = "C:\\sass\\sass.bat",
            ["WorkingDirectory"] = "C:\\repo with spaces",
            ["ProcessArgumentsBase64"] = Encode(arguments),
            ["InputPathsBase64"] = EncodeEach(inputs),
            ["OutputPathsBase64"] = EncodeEach(outputs),
            ["GeneratedPathsBase64"] = EncodeEach(outputs)
        });

        var invocation = Assert.Single(MsBuildWatchConfigurationProvider.ParseTargetResult(json, "C:\\repo\\App.csproj"));

        Assert.Equal("C:\\sass\\src\\dart.exe", invocation.ExecutablePath);
        Assert.Equal("C:\\sass\\sass.bat", invocation.DisplayPath);
        Assert.Equal("C:\\repo with spaces", invocation.WorkingDirectory);
        Assert.Equal(arguments, invocation.ProcessArguments);
        Assert.Equal(inputs, invocation.InputPaths);
        Assert.Equal(outputs, invocation.OutputPaths);
        Assert.Equal(outputs, invocation.GeneratedPaths);
    }

    [Fact]
    public void ParseTargetResult_WithNoGeneratedFilesYet_ShouldReturnAnEmptyList()
    {
        var json = CreateJson(ValidItem(generatedPaths: string.Empty));

        var invocation = Assert.Single(MsBuildWatchConfigurationProvider.ParseTargetResult(json, "App.csproj"));

        Assert.Empty(invocation.GeneratedPaths);
    }

    [Fact]
    public void ParseTargetResult_WithoutGeneratedPathsMetadata_ShouldReturnAnEmptyList()
    {
        var item = ValidItem();
        item.Remove("GeneratedPathsBase64");

        var invocation = Assert.Single(MsBuildWatchConfigurationProvider.ParseTargetResult(CreateJson(item), "App.csproj"));

        Assert.Empty(invocation.GeneratedPaths);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("[]")]
    public void ParseTargetResult_WithNonStringGeneratedPathsMetadata_ShouldFailClearly(string value)
    {
        var item = ValidItem();
        item.Remove("GeneratedPathsBase64");
        var json = CreateJson(item).Replace(
            "\"InputPathsBase64\"",
            $"\"GeneratedPathsBase64\":{value},\"InputPathsBase64\"",
            StringComparison.Ordinal);

        var exception = Assert.Throws<SassWatchException>(
            () => MsBuildWatchConfigurationProvider.ParseTargetResult(json, "App.csproj"));

        Assert.Contains("invalid watch metadata 'GeneratedPathsBase64'", exception.Message);
    }

    [Fact]
    public void ParseTargetResult_WithAnUnknownProtocol_ShouldFailClearly()
    {
        var item = ValidItem();
        item["ProtocolVersion"] = "99";

        var exception = Assert.Throws<SassWatchException>(
            () => MsBuildWatchConfigurationProvider.ParseTargetResult(CreateJson(item), "App.csproj"));

        Assert.Contains("protocol version '99'", exception.Message);
    }

    [Theory]
    [InlineData("ExecutablePath")]
    [InlineData("ProcessArgumentsBase64")]
    [InlineData("WorkingDirectory")]
    [InlineData("InputPathsBase64")]
    public void ParseTargetResult_WithoutRequiredMetadata_ShouldNameIt(string name)
    {
        var item = ValidItem();
        item.Remove(name);

        var exception = Assert.Throws<SassWatchException>(
            () => MsBuildWatchConfigurationProvider.ParseTargetResult(CreateJson(item), "App.csproj"));

        Assert.Contains($"'{name}'", exception.Message);
    }

    [Fact]
    public void ParseTargetResult_WithMismatchedInputsAndOutputs_ShouldFail()
    {
        var item = ValidItem();
        item["OutputPathsBase64"] = EncodeEach(["a.css", "b.css"]);

        var exception = Assert.Throws<SassWatchException>(
            () => MsBuildWatchConfigurationProvider.ParseTargetResult(CreateJson(item), "App.csproj"));

        Assert.Contains("1 Sass input(s) but 2 output(s)", exception.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "TargetResults": {} }""")]
    [InlineData("""{ "TargetResults": { "ResolveSassWatchConfiguration": { "Result": "Success", "Items": [ { "ProtocolVersion": "1", "ExecutablePath": "dart", "DisplayPath": "sass", "WorkingDirectory": "/repo", "ProcessArgumentsBase64": "!!!", "InputPathsBase64": "YQ==", "OutputPathsBase64": "Yg==" } ] } } }""")]
    public void ParseTargetResult_WithMalformedOutput_ShouldFailAsAWatchError(string json)
    {
        var exception = Assert.Throws<SassWatchException>(
            () => MsBuildWatchConfigurationProvider.ParseTargetResult(json, "App.csproj"));

        Assert.Contains("invalid Sass watch configuration", exception.Message);
    }

    [Fact]
    public void ParseTargetResult_WhenTheTargetFailed_ShouldFail()
    {
        const string json =
            """{ "TargetResults": { "ResolveSassWatchConfiguration": { "Result": "Failure" } } }""";

        var exception = Assert.Throws<SassWatchException>(
            () => MsBuildWatchConfigurationProvider.ParseTargetResult(json, "App.csproj"));

        Assert.Contains("did not successfully resolve", exception.Message);
    }

    [Fact]
    public void DescribeMissingTarget_WhenTheWatchTargetDoesNotExist_ShouldExplainWhatTheProjectNeeds()
    {
        const string error =
            "App.csproj : error MSB4057: The target \"ResolveSassWatchConfiguration\" does not exist in the project.";

        var hint = MsBuildWatchConfigurationProvider.DescribeMissingTarget(error);

        Assert.Contains("Scarlet.Sass.MSBuild", hint);
        Assert.Contains("dotnet restore", hint);
    }

    [Theory]
    [InlineData("App.csproj : error CS1002: ; expected")]
    [InlineData("App.csproj : error MSB4057: The target \"Publish\" does not exist in the project.")]
    public void DescribeMissingTarget_ForAnyOtherError_ShouldAddNothing(string error)
    {
        Assert.Equal(string.Empty, MsBuildWatchConfigurationProvider.DescribeMissingTarget(error));
    }

    [Fact]
    public void ResolveProjectPath_ShouldFindTheOnlyProjectInADirectoryAndRejectAmbiguity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scarlet-sass-project-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var missing = Assert.Throws<SassWatchException>(
                () => MsBuildWatchConfigurationProvider.ResolveProjectPath(null, directory));
            Assert.Contains("no project was found", missing.Message);

            var project = Path.Combine(directory, "App.csproj");
            File.WriteAllText(project, "<Project />");
            Assert.Equal(project, MsBuildWatchConfigurationProvider.ResolveProjectPath(null, directory));
            Assert.Equal(project, MsBuildWatchConfigurationProvider.ResolveProjectPath("App.csproj", directory));

            File.WriteAllText(Path.Combine(directory, "Other.fsproj"), "<Project />");
            var ambiguous = Assert.Throws<SassWatchException>(
                () => MsBuildWatchConfigurationProvider.ResolveProjectPath(null, directory));
            Assert.Contains("App.csproj, Other.fsproj", ambiguous.Message);

            var nonexistent = Assert.Throws<SassWatchException>(
                () => MsBuildWatchConfigurationProvider.ResolveProjectPath("nope", directory));
            Assert.Contains("does not exist", nonexistent.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Dictionary<string, string> ValidItem(string? generatedPaths = null)
    {
        return new Dictionary<string, string>
        {
            ["Identity"] = "/repo/Styles/app.scss",
            ["ProtocolVersion"] = "1",
            ["ExecutablePath"] = "/sass/src/dart",
            ["DisplayPath"] = "/sass/sass",
            ["WorkingDirectory"] = "/repo",
            ["ProcessArgumentsBase64"] = Encode("/sass/src/sass.snapshot --update a:b --watch"),
            ["InputPathsBase64"] = EncodeEach(["/repo/Styles/app.scss"]),
            ["OutputPathsBase64"] = EncodeEach(["/repo/wwwroot/css/app.css"]),
            ["GeneratedPathsBase64"] = generatedPaths ?? EncodeEach(["/repo/wwwroot/css/app.css"])
        };
    }

    private static string CreateJson(Dictionary<string, string> item)
    {
        return JsonSerializer.Serialize(new
        {
            TargetResults = new Dictionary<string, object>
            {
                ["ResolveSassWatchConfiguration"] = new { Result = "Success", Items = new[] { item } }
            }
        });
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string EncodeEach(IEnumerable<string> values) => string.Join(";", values.Select(Encode));
}
