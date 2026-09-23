using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Scarlet.Sass.Core;
using Scarlet.Sass.Core.Providers;

namespace Scarlet.Sass.MSBuild;

/// <summary>
/// Compiles Sass entry points before static web asset discovery.
/// </summary>
public sealed class SassCompileTask : Task
{
    private const int DiagnosticTailLineCount = 50;
    private const int OutputDrainGraceMilliseconds = 5000;

    [Required]
    public ITaskItem[] Compilations { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Debug";
    public string OutputStyle { get; set; } = "Auto";
    public string SourceMap { get; set; } = "Auto";
    public string EmbedSources { get; set; } = "Auto";
    public string QuietDeps { get; set; } = "false";
    public string LoadPaths { get; set; } = string.Empty;
    public string PkgImporter { get; set; } = string.Empty;
    public string SilenceDeprecations { get; set; } = string.Empty;
    public string FatalDeprecations { get; set; } = string.Empty;
    public string AdditionalArguments { get; set; } = string.Empty;
    public string StampDirectory { get; set; } = string.Empty;
    public string? RuntimeDirectory { get; set; }
    public bool SassRuntimeDownload { get; set; }
    public string? SassVersionDownload { get; set; }
    public int DownloadMutexTimeoutSeconds { get; set; } = 300;
    public ITaskItem[]? RuntimePacks { get; set; }

    /// <summary>
    /// Maximum time, in milliseconds, to wait for each `sass` invocation before killing it. Zero (the
    /// default) waits indefinitely, matching the pre-existing behavior.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 0;

    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = Array.Empty<ITaskItem>();

    [Output]
    public ITaskItem[] RemovedFiles { get; private set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        // Closed on every exit path. A handler firing after the task has returned - possible whenever the
        // drain grace expires - would otherwise log into a finished task, which MSBuild turns into an
        // exception that AsyncStreamReader rethrows on a thread-pool thread, killing the build process.
        var gate = new TaskLifetimeGate();

        try
        {
            var fileSystem = new FileSystem();
            var settings = ResolveSettings(
                OutputStyle,
                SourceMap,
                EmbedSources,
                QuietDeps,
                SplitList(LoadPaths),
                EmptyToNull(PkgImporter),
                SplitList(SilenceDeprecations),
                SplitList(FatalDeprecations),
                AdditionalArguments ?? string.Empty);
            if (Log.HasLoggedErrors)
            {
                return false;
            }

            var entries = DiscoverEntries(fileSystem);
            if (Log.HasLoggedErrors)
            {
                return false;
            }

            if (entries.Count == 0)
            {
                Log.LogMessage(MessageImportance.Low, "No Sass entry points were found.");
                return true;
            }

            var sassCommand = ResolveSass(fileSystem);
            var stampDirectory = ResolveStampDirectory();
            var manifestPath = Path.Combine(stampDirectory, "Sass.generated.txt");
            var stampPath = Path.Combine(stampDirectory, "Sass.settings.stamp");
            var stampContent = CreateStampContent(settings, entries);
            var forceRegenerate = IsStampStale(fileSystem, stampPath, stampContent);
            var expectedFiles = entries.SelectMany(static entry => entry.ExpectedOutputs).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var previousFiles = ReadManifest(fileSystem, manifestPath);
            var removedFiles = previousFiles.Except(expectedFiles, StringComparer.OrdinalIgnoreCase).ToArray();

            if (forceRegenerate)
            {
                foreach (var output in expectedFiles)
                {
                    DeleteIfExists(fileSystem, output);
                }
            }

            foreach (var stale in removedFiles)
            {
                DeleteIfExists(fileSystem, stale);
            }

            Log.LogMessage(MessageImportance.High, $"Using Sass at: {sassCommand.DisplayPath}");

            foreach (var group in entries.GroupBy(static entry => entry.Settings.ToString()))
            {
                var groupedEntries = group.ToArray();
                var arguments = BuildArguments(groupedEntries[0].Settings, groupedEntries);
                var processArguments = SassCommandLine.BuildProcessArguments(sassCommand, arguments);
                Log.LogMessage(MessageImportance.High, $"Executing: {SassCommandLine.FormatProcessCommand(sassCommand, processArguments)}");

                var result = RunProcess(sassCommand, processArguments, gate);
                if (result is null)
                {
                    return false;
                }

                if (result.ExitCode != 0)
                {
                    Log.LogError($"Sass command failed with exit code {result.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(result.ErrorTail))
                    {
                        Log.LogError(result.ErrorTail);
                    }

                    if (!string.IsNullOrWhiteSpace(result.OutputTail))
                    {
                        Log.LogError(result.OutputTail);
                    }

                    return false;
                }
            }

            fileSystem.Directory.CreateDirectory(stampDirectory);
            fileSystem.File.WriteAllText(stampPath, stampContent);
            fileSystem.File.WriteAllLines(manifestPath, expectedFiles);

            GeneratedFiles = expectedFiles.Select(CreateGeneratedFileItem).ToArray();
            RemovedFiles = removedFiles.Select(static path => (ITaskItem)new TaskItem(path)).ToArray();

            return !Log.HasLoggedErrors;
        }
        catch (FileNotFoundException ex)
        {
            Log.LogError(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
        finally
        {
            gate.Close();
        }
    }

    private SassSettings ResolveSettings(
        string outputStyleValue,
        string sourceMapValue,
        string embedSourcesValue,
        string quietDepsValue,
        IReadOnlyList<string> loadPaths,
        string? pkgImporter,
        IReadOnlyList<string> silenceDeprecations,
        IReadOnlyList<string> fatalDeprecations,
        string additionalArguments)
    {
        var debug = string.Equals(Configuration, "Debug", StringComparison.OrdinalIgnoreCase);
        var outputStyle = debug ? "expanded" : "compressed";
        var sourceMap = debug;
        var embedSources = debug;

        if (!string.Equals(outputStyleValue, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(outputStyleValue, "Expanded", StringComparison.OrdinalIgnoreCase))
            {
                outputStyle = "expanded";
            }
            else if (string.Equals(outputStyleValue, "Compressed", StringComparison.OrdinalIgnoreCase))
            {
                outputStyle = "compressed";
            }
            else
            {
                Log.LogError("SassOutputStyle must be Auto, Expanded, or Compressed; received '{0}'.", outputStyleValue);
            }
        }

        sourceMap = ParseAutoBoolean(sourceMapValue, sourceMap, "SassSourceMap");
        embedSources = ParseAutoBoolean(embedSourcesValue, embedSources, "SassEmbedSources");
        var quietDeps = ParseBoolean(quietDepsValue, "SassQuietDeps");

        return new SassSettings(
            outputStyle,
            sourceMap,
            embedSources,
            quietDeps,
            loadPaths,
            pkgImporter,
            silenceDeprecations,
            fatalDeprecations,
            additionalArguments);
    }

    private List<SassEntry> DiscoverEntries(IFileSystem fileSystem)
    {
        var entries = new List<SassEntry>();
        var projectDirectory = Path.GetFullPath(ProjectDirectory);

        foreach (var item in Compilations)
        {
            var input = ResolvePath(projectDirectory, item.ItemSpec);
            var outputMetadata = item.GetMetadata("OutputPath");
            if (string.IsNullOrWhiteSpace(outputMetadata))
            {
                Log.LogError("SassBeforeStaticWebAssets item '{0}' must specify OutputPath metadata.", item.ItemSpec);
                continue;
            }

            var output = ResolvePath(projectDirectory, outputMetadata);
            var itemSettings = ResolveSettings(
                Override(item.GetMetadata("OutputStyle"), OutputStyle),
                Override(item.GetMetadata("SourceMap"), SourceMap),
                Override(item.GetMetadata("EmbedSources"), EmbedSources),
                Override(item.GetMetadata("QuietDeps"), QuietDeps),
                SplitList(LoadPaths).Concat(SplitList(item.GetMetadata("LoadPaths"))).ToArray(),
                EmptyToNull(Override(item.GetMetadata("PkgImporter"), PkgImporter)),
                SplitList(SilenceDeprecations).Concat(SplitList(item.GetMetadata("SilenceDeprecations"))).ToArray(),
                SplitList(FatalDeprecations).Concat(SplitList(item.GetMetadata("FatalDeprecations"))).ToArray(),
                string.Join(" ", new[] { AdditionalArguments, item.GetMetadata("AdditionalArguments") }.Where(static value => !string.IsNullOrWhiteSpace(value))));

            if (fileSystem.Directory.Exists(input))
            {
                if (fileSystem.File.Exists(output))
                {
                    Log.LogError("OutputPath for Sass directory input '{0}' must be a directory, but '{1}' is a file.", item.ItemSpec, outputMetadata);
                    continue;
                }

                var expected = DiscoverDirectoryOutputs(fileSystem, input, output, itemSettings.SourceMap);
                entries.Add(new SassEntry(input, output, true, expected, itemSettings));
            }
            else if (fileSystem.File.Exists(input))
            {
                if (IsPartial(input))
                {
                    continue;
                }

                var cssOutput = output.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                    ? output
                    : Path.ChangeExtension(output, ".css");
                var expected = itemSettings.SourceMap
                    ? new[] { cssOutput, cssOutput + ".map" }
                    : new[] { cssOutput };
                entries.Add(new SassEntry(input, cssOutput, false, expected, itemSettings));
            }
            else
            {
                Log.LogError("Sass input '{0}' was not found.", item.ItemSpec);
            }
        }

        return entries;
    }

    private static IReadOnlyList<string> DiscoverDirectoryOutputs(IFileSystem fileSystem, string inputDirectory, string outputDirectory, bool sourceMap)
    {
        var outputs = new List<string>();
        foreach (var file in fileSystem.Directory.EnumerateFiles(inputDirectory, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".scss", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".sass", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsPartial(file))
            {
                continue;
            }

            var relative = file.Substring(inputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var css = Path.ChangeExtension(Path.Combine(outputDirectory, relative), ".css");
            outputs.Add(css);
            if (sourceMap)
            {
                outputs.Add(css + ".map");
            }
        }

        return outputs;
    }

    private SassLaunchCommand ResolveSass(IFileSystem fileSystem)
    {
        var chmodProvider = Chmod.CreateProvider();

        if (SassRuntimeDownload)
        {
            if (string.IsNullOrWhiteSpace(RuntimeDirectory))
            {
                throw new FileNotFoundException("SassRuntimeDirectory is required when SassRuntimeDownload is true.");
            }

            using var httpClient = SassDownloader.CreateHttpClient();
            var downloader = new SassDownloader(
                httpClient,
                new GitHubLatestVersionResolver(),
                fileSystem,
                ZipArchiveProvider.Instance,
                TarArchiveProvider.Instance,
                chmodProvider,
                SassRuntimeResolver.GetCurrentPlatform(),
                new MsBuildSassLogger(Log));
            var sassPath = downloader.DownloadRuntime(RuntimeDirectory!, SassVersionDownload, DownloadMutexTimeoutSeconds);

            return SassRuntimeResolver.CreateLaunchCommand(fileSystem, sassPath, SassRuntimeResolver.GetCurrentPlatform());
        }

        return SassRuntimeResolver.ResolveSassLaunchCommand(
            fileSystem,
            chmodProvider,
            SassRuntimeResolver.GetCurrentPlatform(),
            RuntimeDirectory,
            SassRuntimePackFactory.FromTaskItems(RuntimePacks, warning => Log.LogWarning(warning)),
            message => Log.LogMessage(MessageImportance.Normal, message));
    }

    private string BuildArguments(SassSettings globalSettings, IReadOnlyList<SassEntry> entries)
    {
        var args = new List<string>
        {
            "--update",
            $"--style={globalSettings.OutputStyle}"
        };

        if (!globalSettings.SourceMap)
        {
            args.Add("--no-source-map");
        }
        else if (globalSettings.EmbedSources)
        {
            args.Add("--embed-sources");
        }

        if (globalSettings.QuietDeps)
        {
            args.Add("--quiet-deps");
        }

        foreach (var loadPath in globalSettings.LoadPaths)
        {
            args.Add($"--load-path={SassCommandLine.QuoteArgument(loadPath)}");
        }

        if (!string.IsNullOrWhiteSpace(globalSettings.PkgImporter))
        {
            args.Add($"--pkg-importer={globalSettings.PkgImporter}");
        }

        foreach (var deprecation in globalSettings.SilenceDeprecations)
        {
            args.Add($"--silence-deprecation={deprecation}");
        }

        foreach (var deprecation in globalSettings.FatalDeprecations)
        {
            args.Add($"--fatal-deprecation={deprecation}");
        }

        if (!string.IsNullOrWhiteSpace(globalSettings.AdditionalArguments))
        {
            args.Add(globalSettings.AdditionalArguments);
        }

        foreach (var entry in entries)
        {
            args.Add($"{SassCommandLine.QuoteArgument(entry.InputPath)}:{SassCommandLine.QuoteArgument(entry.OutputPath)}");
        }

        return string.Join(" ", args);
    }

    private ProcessResult? RunProcess(SassLaunchCommand command, string processArguments, TaskLifetimeGate gate)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = processArguments,
            WorkingDirectory = ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process();
        process.StartInfo = startInfo;
        var output = new OutputCollector(DiagnosticTailLineCount, captureAll: false);
        var error = new OutputCollector(DiagnosticTailLineCount, captureAll: false);

        // Declared out here, not beside the handlers that use them: a `using` inside the try disposes as
        // control leaves the try, which is before the finally closes the gate - leaving a window where a late
        // handler could pass the gate and signal a disposed event. At method scope they outlive the gate.
        using var outputClosed = new ManualResetEventSlim(false);
        using var errorClosed = new ManualResetEventSlim(false);

        // Neither handler may touch `process`. It is disposed as control leaves this method, while these can
        // still fire, so reaching for something like process.Id here would hit a disposed object on a
        // thread-pool thread - which terminates the build. Nothing tests this; it only holds by inspection.
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                // ReSharper disable once AccessToDisposedClosure
                gate.TryRun(outputClosed.Set);
                return;
            }

            output.Add(e.Data);
            gate.TryRun(() => Log.LogMessage(MessageImportance.Normal, e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                // ReSharper disable once AccessToDisposedClosure
                gate.TryRun(errorClosed.Set);
                return;
            }

            error.Add(e.Data);
            gate.TryRun(() => Log.LogMessage(MessageImportance.High, e.Data));
        };

        ProcessStartRetry.Start(process, new MsBuildSassLogger(Log));
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (TimeoutMilliseconds > 0)
        {
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Ignore if process already exited.
                }

                Log.LogError($"Command timed out after {TimeoutMilliseconds}ms");
                return null;
            }

            // The process is gone, but the handlers may not have drained. Waiting on the end-of-stream
            // signals rather than the parameterless WaitForExit() keeps that wait bounded - a detached
            // grandchild holding the write end can withhold EOF forever, outliving the timeout the caller
            // asked for - and costs no extra thread to abandon when it does.
            if (!(outputClosed.Wait(OutputDrainGraceMilliseconds) && errorClosed.Wait(OutputDrainGraceMilliseconds)))
            {
                Log.LogMessage(
                    MessageImportance.Normal,
                    $"Sass exited but its output was still open after {OutputDrainGraceMilliseconds}ms; some output may be missing.");
            }
        }
        else
        {
            process.WaitForExit();
        }

        return new ProcessResult(process.ExitCode, output.Tail, error.Tail);
    }

    private string ResolveStampDirectory()
    {
        if (!string.IsNullOrWhiteSpace(StampDirectory))
        {
            return ResolvePath(ProjectDirectory, StampDirectory);
        }

        return Path.Combine(ProjectDirectory, "obj", "Scarlet.Sass");
    }

    private static bool IsStampStale(IFileSystem fileSystem, string stampPath, string content)
    {
        return !fileSystem.File.Exists(stampPath)
               || !string.Equals(fileSystem.File.ReadAllText(stampPath), content, StringComparison.Ordinal);
    }

    private string CreateStampContent(SassSettings settings, IReadOnlyList<SassEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Scarlet.Sass.MSBuild settings stamp");
        builder.AppendLine($"Configuration={Configuration}");
        builder.AppendLine($"RuntimeDirectory={RuntimeDirectory}");
        builder.AppendLine($"RuntimeDownload={SassRuntimeDownload}");
        builder.AppendLine($"VersionDownload={SassVersionDownload}");
        builder.AppendLine(settings.ToString());

        foreach (var pack in RuntimePacks ?? Array.Empty<ITaskItem>())
        {
            builder.AppendLine(string.Join(
                "|",
                pack.ItemSpec,
                pack.GetMetadata(SassRuntimePack.RidMetadataName),
                pack.GetMetadata(SassRuntimePack.RuntimesPathMetadataName),
                pack.GetMetadata(SassRuntimePack.PriorityMetadataName)));
        }

        foreach (var entry in entries)
        {
            builder.AppendLine($"{entry.InputPath}|{entry.OutputPath}|{entry.Settings}");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ReadManifest(IFileSystem fileSystem, string manifestPath)
    {
        return fileSystem.File.Exists(manifestPath)
            ? fileSystem.File.ReadAllLines(manifestPath)
            : Array.Empty<string>();
    }

    private static void DeleteIfExists(IFileSystem fileSystem, string path)
    {
        if (fileSystem.File.Exists(path))
        {
            fileSystem.File.Delete(path);
        }
    }

    private bool ParseAutoBoolean(string value, bool automatic, string name)
    {
        if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return automatic;
        }

        return ParseBoolean(value, name);
    }

    private bool ParseBoolean(string value, string name)
    {
        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        Log.LogError("{0} must be true or false; received '{1}'.", name, value);
        return false;
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        var normalizedPath = NormalizePathSeparators(path);

        return Path.IsPathRooted(normalizedPath)
            ? Path.GetFullPath(normalizedPath)
            : Path.GetFullPath(Path.Combine(baseDirectory, normalizedPath));
    }

    private static string NormalizePathSeparators(string path)
    {
        return Path.DirectorySeparatorChar == '\\'
            ? path.Replace('/', Path.DirectorySeparatorChar)
            : path.Replace('\\', Path.DirectorySeparatorChar);
    }

    private ITaskItem CreateGeneratedFileItem(string path)
    {
        var item = new TaskItem(path);
        item.SetMetadata("RelativePath", GetRelativePath(ProjectDirectory, path));
        return item;
    }

    private static string GetRelativePath(string baseDirectory, string path)
    {
        var baseUri = new Uri(EnsureTrailingDirectorySeparator(Path.GetFullPath(baseDirectory)));
        var pathUri = new Uri(Path.GetFullPath(path));

        if (!string.Equals(baseUri.Scheme, pathUri.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
        return relative.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
               || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsPartial(string path) => Path.GetFileName(path).StartsWith("_", StringComparison.Ordinal);

    private static string[] SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(static item => item.Trim()).Where(static item => item.Length > 0).ToArray();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static string Override(string metadata, string fallback) => string.IsNullOrWhiteSpace(metadata) ? fallback : metadata;

    private sealed class SassSettings
    {
        public SassSettings(
            string outputStyle,
            bool sourceMap,
            bool embedSources,
            bool quietDeps,
            IReadOnlyList<string> loadPaths,
            string? pkgImporter,
            IReadOnlyList<string> silenceDeprecations,
            IReadOnlyList<string> fatalDeprecations,
            string additionalArguments)
        {
            OutputStyle = outputStyle;
            SourceMap = sourceMap;
            EmbedSources = embedSources;
            QuietDeps = quietDeps;
            LoadPaths = loadPaths;
            PkgImporter = pkgImporter;
            SilenceDeprecations = silenceDeprecations;
            FatalDeprecations = fatalDeprecations;
            AdditionalArguments = additionalArguments;
        }

        public string OutputStyle { get; }
        public bool SourceMap { get; }
        public bool EmbedSources { get; }
        public bool QuietDeps { get; }
        public IReadOnlyList<string> LoadPaths { get; }
        public string? PkgImporter { get; }
        public IReadOnlyList<string> SilenceDeprecations { get; }
        public IReadOnlyList<string> FatalDeprecations { get; }
        public string AdditionalArguments { get; }

        public override string ToString()
        {
            return string.Join(
                "|",
                OutputStyle,
                SourceMap.ToString(),
                EmbedSources.ToString(),
                QuietDeps.ToString(),
                string.Join(";", LoadPaths),
                PkgImporter ?? string.Empty,
                string.Join(";", SilenceDeprecations),
                string.Join(";", FatalDeprecations),
                AdditionalArguments);
        }
    }

    private sealed class SassEntry
    {
        public SassEntry(
            string inputPath,
            string outputPath,
            bool isDirectory,
            IReadOnlyList<string> expectedOutputs,
            SassSettings settings)
        {
            InputPath = inputPath;
            OutputPath = outputPath;
            IsDirectory = isDirectory;
            ExpectedOutputs = expectedOutputs;
            Settings = settings;
        }

        public string InputPath { get; }
        public string OutputPath { get; }
        public bool IsDirectory { get; }
        public IReadOnlyList<string> ExpectedOutputs { get; }
        public SassSettings Settings { get; }
    }

    private sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string outputTail, string errorTail)
        {
            ExitCode = exitCode;
            OutputTail = outputTail;
            ErrorTail = errorTail;
        }

        public int ExitCode { get; }
        public string OutputTail { get; }
        public string ErrorTail { get; }
    }
}
