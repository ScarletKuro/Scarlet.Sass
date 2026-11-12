using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Scarlet.Sass.Cli.Watch;

/// <summary>
/// Asks the consuming project to resolve the exact Dart Sass command lines used by its build.
/// </summary>
internal sealed class MsBuildWatchConfigurationProvider : ISassWatchConfigurationProvider
{
    private const string TargetName = "ResolveSassWatchConfiguration";
    private const string ProtocolVersion = "1";

    [ExcludeFromCodeCoverage]
    public IReadOnlyList<SassWatchInvocation> Resolve(
        string? project,
        string configuration,
        string currentDirectory)
    {
        var projectPath = ResolveProjectPath(project, currentDirectory);
        var startInfo = new ProcessStartInfo(ResolveDotnetPath())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!
        };

        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:quiet");
        startInfo.ArgumentList.Add($"-getTargetResult:{TargetName}");
        startInfo.ArgumentList.Add($"-property:Configuration={configuration}");
        using var process = Process.Start(startInfo)
            ?? throw new SassWatchException("could not start dotnet msbuild.", ExitCodes.SassNotExecutable);

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult().Trim();

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error)
                ? $"MSBuild exited with code {process.ExitCode}."
                : error;
            throw new SassWatchException(
                $"could not read Sass configuration from '{projectPath}'. {detail}{DescribeMissingTarget(error)}");
        }

        return ParseTargetResult(output, projectPath);
    }

    internal static IReadOnlyList<SassWatchInvocation> ParseTargetResult(string json, string projectPath)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var target = document.RootElement
                .GetProperty("TargetResults")
                .GetProperty(TargetName);

            if (!string.Equals(target.GetProperty("Result").GetString(), "Success", StringComparison.Ordinal))
            {
                throw new SassWatchException(
                    $"MSBuild did not successfully resolve Sass configuration for '{projectPath}'.");
            }

            var result = new List<SassWatchInvocation>();
            if (!target.TryGetProperty("Items", out var items))
            {
                return result;
            }

            foreach (var item in items.EnumerateArray())
            {
                var protocolVersion = GetRequiredString(item, "ProtocolVersion");
                if (!string.Equals(protocolVersion, ProtocolVersion, StringComparison.Ordinal))
                {
                    throw new SassWatchException(
                        $"the project returned unsupported Sass watch protocol version '{protocolVersion}'. "
                        + "Use matching versions of the Scarlet.Sass tool and the Scarlet.Sass.MSBuild package.");
                }

                var inputPaths = DecodeEach(GetRequiredString(item, "InputPathsBase64"));
                var outputPaths = DecodeEach(GetRequiredString(item, "OutputPathsBase64"));
                if (inputPaths.Count != outputPaths.Count)
                {
                    throw new SassWatchException(
                        $"MSBuild returned {inputPaths.Count} Sass input(s) but {outputPaths.Count} output(s).");
                }

                result.Add(new SassWatchInvocation(
                    GetRequiredString(item, "ExecutablePath"),
                    DecodeOne(GetRequiredString(item, "ProcessArgumentsBase64")),
                    GetRequiredString(item, "WorkingDirectory"),
                    GetRequiredString(item, "DisplayPath"),
                    inputPaths,
                    outputPaths,
                    // A directory input whose stylesheets have never been compiled legitimately has none yet.
                    DecodeEach(GetOptionalString(item, "GeneratedPathsBase64"))));
            }

            return result;
        }
        catch (SassWatchException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new SassWatchException(
                $"MSBuild returned an invalid Sass watch configuration for '{projectPath}': {exception.Message}");
        }
    }

    /// <summary>
    /// Explains the one MSBuild failure that is about this command rather than the project: the target does
    /// not exist because the package is not referenced, not restored, or predates the watch command.
    /// </summary>
    internal static string DescribeMissingTarget(string error)
    {
        return error.Contains("MSB4057", StringComparison.Ordinal)
            && error.Contains(TargetName, StringComparison.Ordinal)
            ? Environment.NewLine
              + "dotnet sass watch requires the project to reference a Scarlet.Sass.MSBuild version that "
              + "provides this target, and to have been restored (run 'dotnet restore')."
            : string.Empty;
    }

    internal static string ResolveProjectPath(string? project, string currentDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(project)
            ? currentDirectory
            : Path.GetFullPath(project, currentDirectory);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        if (!Directory.Exists(candidate))
        {
            throw new SassWatchException($"project path '{candidate}' does not exist.");
        }

        var projects = new[] { "*.csproj", "*.fsproj", "*.vbproj" }
            .SelectMany(pattern => Directory.EnumerateFiles(candidate, pattern, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return projects.Length switch
        {
            1 => projects[0],
            0 => throw new SassWatchException($"no project was found in '{candidate}'. Use --project to specify one."),
            _ => throw new SassWatchException(
                $"multiple projects were found in '{candidate}'. Use --project to specify one: "
                + string.Join(", ", projects.Select(Path.GetFileName)))
        };
    }

    [ExcludeFromCodeCoverage]
    private static string ResolveDotnetPath()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath))
        {
            return hostPath;
        }

        // A local tool runs under the dotnet muxer, so this is the exact host that launched us even when it is
        // not on PATH. A global tool runs through its apphost shim instead, which must not be mistaken for it.
        var processPath = Environment.ProcessPath;
        if (processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        return "dotnet";
    }

    private static string DecodeOne(string encoded) => Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

    private static List<string> DecodeEach(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? []
            : value.Split(';').Select(DecodeOne).ToList();
    }

    private static string GetRequiredString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new SassWatchException($"MSBuild omitted required watch metadata '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static string? GetOptionalString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new SassWatchException($"MSBuild returned invalid watch metadata '{propertyName}'.");
        }

        return property.GetString();
    }
}
