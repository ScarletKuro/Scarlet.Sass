using Microsoft.Build.Utilities;
using Xunit.Abstractions;

namespace Scarlet.Sass.MSBuild.IntegrationTests;

public class SassIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public SassIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void SassRunTask_ShouldExecuteBuildScript()
    {
        using var workspace = TestAssetWorkspace.Create(_output);
        var testAssetsDir = workspace.RootDirectory;
        var buildScriptPath = workspace.BuildScriptPath;
        var outputDir = workspace.OutputDirectory;
        var jsOutputFile = Path.Combine(outputDir, "Sassdle.min.js");
        var cssOutputFile = Path.Combine(outputDir, "style.min.css");

        // Clean up any previous output
        if (Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }

        _output.WriteLine($"Test assets directory: {testAssetsDir}");
        _output.WriteLine($"Build script: {buildScriptPath}");
        
        Assert.True(Directory.Exists(testAssetsDir), $"Test assets directory not found: {testAssetsDir}");
        Assert.True(File.Exists(buildScriptPath), $"Build script not found: {buildScriptPath}");

        // First, install dependencies using Sass
        _output.WriteLine("Installing dependencies with Sass...");
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        _output.WriteLine($"Runtime directory: {runtimesDirectory}");
        
        var installTask = new SassRunTask
        {
            Command = "ci",
            WorkingDirectory = testAssetsDir,
            RuntimeDirectory = runtimesDirectory,
            BuildEngine = new MockBuildEngine(_output)
        };

        var installResult = installTask.Execute();
        _output.WriteLine($"Install result: {installResult}");
        _output.WriteLine($"Install exit code: {installTask.ExitCode}");
        if (!string.IsNullOrEmpty(installTask.StandardOutput))
        {
            _output.WriteLine($"Install output: {installTask.StandardOutput}");
        }
        if (!string.IsNullOrEmpty(installTask.StandardError))
        {
            _output.WriteLine($"Install error: {installTask.StandardError}");
        }

        Assert.True(installResult, "Sass ci failed");
        Assert.Equal(0, installTask.ExitCode);

        // Act - Execute the build script
        _output.WriteLine("Running build script with Sass...");
        var task = new SassRunTask
        {
            Command = "run",
            Arguments = "build.mjs",
            WorkingDirectory = testAssetsDir,
            RuntimeDirectory = runtimesDirectory,
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();
        _output.WriteLine($"Build result: {result}");
        _output.WriteLine($"Build exit code: {task.ExitCode}");
        if (!string.IsNullOrEmpty(task.StandardOutput))
        {
            _output.WriteLine($"Build output: {task.StandardOutput}");
        }
        if (!string.IsNullOrEmpty(task.StandardError))
        {
            _output.WriteLine($"Build error: {task.StandardError}");
        }

        // Assert
        Assert.True(result, "Sass run command failed");
        Assert.Equal(0, task.ExitCode);

        // Verify output files were created
        Assert.True(File.Exists(jsOutputFile), $"JS output file not created: {jsOutputFile}");
        Assert.True(File.Exists(cssOutputFile), $"CSS output file not created: {cssOutputFile}");

        // Verify JS Sassdle content
        var jsContent = File.ReadAllText(jsOutputFile);
        _output.WriteLine($"JS Sassdle size: {jsContent.Length} bytes");
        Assert.NotEmpty(jsContent);
        Assert.Contains("hello", jsContent); // Should contain minified version of our functions
        Assert.Contains("world", jsContent);

        // Verify CSS Sassdle content
        var cssContent = File.ReadAllText(cssOutputFile);
        _output.WriteLine($"CSS Sassdle size: {cssContent.Length} bytes");
        Assert.NotEmpty(cssContent);
        Assert.Contains("body", cssContent);
        Assert.Contains(".button", cssContent);
    }

    [Fact]
    public void SassRunTask_CanBeInvokedAndResolvesRuntime()
    {
        // This test verifies that:
        // 1. The SassRunTask can be instantiated
        // 2. It can resolve the Sass runtime path
        // 3. It attempts to execute Sass (even if Sass crashes, our task works)
        
        using var workspace = TestAssetWorkspace.Create(_output);
        var testAssetsDir = workspace.RootDirectory;
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        
        _output.WriteLine($"Test assets directory: {testAssetsDir}");
        _output.WriteLine($"Runtime directory: {runtimesDirectory}");
        _output.WriteLine($"Current platform: {SassRuntimeResolver.GetCurrentPlatform()}");
        
        // Create a simple task that will try to get Sass version
        var task = new SassRunTask
        {
            Command = "run",
            WorkingDirectory = testAssetsDir,
            RuntimeDirectory = runtimesDirectory,
            BuildEngine = new MockBuildEngine(_output),
            ContinueOnError = true // Don't fail if Sass has issues
        };

        // Execute - this tests that our task can find and attempt to execute Sass
        var result = task.Execute();
        
        _output.WriteLine($"Task execution completed: {result}");
        _output.WriteLine($"Exit code: {task.ExitCode}");
        if (!string.IsNullOrEmpty(task.StandardOutput))
        {
            _output.WriteLine($"Output: {task.StandardOutput}");
        }
        if (!string.IsNullOrEmpty(task.StandardError))
        {
            _output.WriteLine($"Error: {task.StandardError}");
        }
        
        // ContinueOnError=true should keep the task successful even if Sass exits non-zero.
        Assert.True(result, "Task should return true when ContinueOnError is enabled.");
        Assert.NotEqual(-1, task.ExitCode);
        Assert.False(
            string.IsNullOrWhiteSpace(task.StandardOutput) && string.IsNullOrWhiteSpace(task.StandardError),
            "Expected Sass invocation to produce stdout or stderr.");
    }

    [Fact]
    public void SassRunTask_WithInvalidCommand_ShouldFail()
    {
        // Arrange
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var task = new SassRunTask
        {
            Command = "invalid-command-that-does-not-exist-xyz123",
            RuntimeDirectory = runtimesDirectory,
            BuildEngine = new MockBuildEngine(_output)
        };

        // Act
        var result = task.Execute();

        // Assert
        Assert.False(result);
        Assert.NotEqual(0, task.ExitCode);
    }

    [Fact]
    public void SassRunTask_WithUpToDateInputsAndOutputs_ShouldSkipBeforeResolvingRuntime()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-incremental-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var newerInput = Path.Combine(tempDir, "newer-input.js");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(newerInput, "console.log('newer input');");
            File.WriteAllText(output, "console.log('output');");

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(input, now.AddMinutes(-10));
            File.SetLastWriteTimeUtc(newerInput, now.AddMinutes(-5));
            File.SetLastWriteTimeUtc(output, now);

            var firstRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = $"{input};{newerInput}",
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(firstRun.Execute());
            Assert.Equal(0, firstRun.ExitCode);

            var buildEngine = new MockBuildEngine(_output);
            var task = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                // Deliberately the same runtime directory as the first run: RuntimeDirectory is part of the
                // stamp, so pointing this somewhere invalid to prove the skip would invalidate the stamp and
                // prove the opposite. That resolution never happened is asserted directly below instead.
                RuntimeDirectory = runtimesDirectory,
                Inputs = $"{input};{newerInput}",
                Outputs = output,
                BuildEngine = buildEngine
            };

            var result = task.Execute();

            Assert.True(result);
            Assert.Equal(0, task.ExitCode);
            Assert.Empty(buildEngine.Errors);
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("Using Sass at", StringComparison.Ordinal) == true);

            var changedCommandEngine = new MockBuildEngine(_output);
            var changedCommand = new SassRunTask
            {
                Command = "--help",
                WorkingDirectory = tempDir,
                RuntimeDirectory = "invalid-runtime-directory",
                Inputs = $"{input};{newerInput}",
                Outputs = output,
                BuildEngine = changedCommandEngine
            };

            Assert.False(changedCommand.Execute());
            Assert.DoesNotContain(changedCommandEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);

            // Arguments are as much a part of what produced the outputs as the command is; `Sass run a.mjs`
            // and `Sass run b.mjs` must not share a stamp.
            var changedArgumentsEngine = new MockBuildEngine(_output);
            var changedArguments = new SassRunTask
            {
                Command = "--version",
                Arguments = "--extra",
                WorkingDirectory = tempDir,
                RuntimeDirectory = "invalid-runtime-directory",
                Inputs = $"{input};{newerInput}",
                Outputs = output,
                BuildEngine = changedArgumentsEngine
            };

            Assert.False(changedArguments.Execute());
            Assert.DoesNotContain(changedArgumentsEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);

            var uncapturedSkip = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = $"{input};{newerInput}",
                Outputs = output,
                CaptureOutput = false,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(uncapturedSkip.Execute());
            Assert.Null(uncapturedSkip.StandardOutput);
            Assert.Null(uncapturedSkip.StandardError);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SassRunTask_WithNoWorkingDirectory_ShouldUseCurrentDirectoryForDefaultStampPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-current-dir-{Guid.NewGuid():N}");
        var currentDirectoryStampDir = Path.Combine(Directory.GetCurrentDirectory(), "obj", "Scarlet.Sass");
        var existingStamps = Directory.Exists(currentDirectoryStampDir)
            ? Directory.GetFiles(currentDirectoryStampDir, "*.stamp").ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var createdStamps = new List<string>();
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(output, "console.log('output');");

            var firstRun = new SassRunTask
            {
                Command = "--version",
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(firstRun.Execute());
            createdStamps = Directory.GetFiles(currentDirectoryStampDir, "*.stamp")
                .Where(path => !existingStamps.Contains(path))
                .ToList();
            Assert.Single(createdStamps);

            var buildEngine = new MockBuildEngine(_output);
            var secondRun = new SassRunTask
            {
                Command = "--version",
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = buildEngine
            };

            Assert.True(secondRun.Execute());
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            foreach (var stamp in createdStamps)
            {
                try
                {
                    File.Delete(stamp);
                }
                catch
                {
                    // Best-effort cleanup for the explicit current-directory stamp assertion.
                }
            }

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SassRunTask_WithMissingIncrementalInput_ShouldNotSkip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-missing-input-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(output, "console.log('output');");
            var buildEngine = new MockBuildEngine(_output);
            var task = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = "invalid-runtime-directory",
                Inputs = Path.Combine(tempDir, "missing-input.js"),
                Outputs = output,
                BuildEngine = buildEngine
            };

            Assert.False(task.Execute());
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

        /// <remarks>
    /// The directory output case is the documented <c>Sass install</c> shape - <c>Outputs=node_modules</c> -
    /// where the up-to-date check has nothing but a directory's existence to go on.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SassRunTask_WithUnchangedDirectoryInput_ShouldSkip(bool outputIsDirectory)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-directory-input-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var inputDirectory = Path.Combine(tempDir, "assets");
            Directory.CreateDirectory(inputDirectory);

            var output = Path.Combine(tempDir, outputIsDirectory ? "node_modules" : "Sassdle.js");
            if (outputIsDirectory)
            {
                Directory.CreateDirectory(output);
            }
            else
            {
                File.WriteAllText(output, "console.log('output');");
            }

            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");

            var firstRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = inputDirectory,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(firstRun.Execute());

            var buildEngine = new MockBuildEngine(_output);
            var secondRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = inputDirectory,
                Outputs = output,
                BuildEngine = buildEngine
            };

            Assert.True(secondRun.Execute());
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// A directory's own timestamp only moves when an entry is added or removed, so editing a file in place -
    /// the ordinary case for a source tree handed to Sass - would leave a shallow check believing the step is
    /// up to date and ship stale outputs. Nested files are covered too, since a subdirectory's timestamp does
    /// not propagate to its parent either.
    /// </summary>
    [Theory]
    [InlineData("changed.js")]
    [InlineData("nested/changed.js")]
    public void SassRunTask_WithEditedFileUnderDirectoryInput_ShouldNotSkip(string relativePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-directory-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var inputDirectory = Path.Combine(tempDir, "assets");
            var editedFile = Path.Combine(inputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(editedFile)!);
            File.WriteAllText(editedFile, "console.log('v1');");

            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(output, "console.log('output');");
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");

            var firstRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = inputDirectory,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(firstRun.Execute());

            // Edit in place without touching the directory's entry list, and push the timestamp past the
            // stamp so the result cannot depend on the two landing in the same filesystem tick.
            File.WriteAllText(editedFile, "console.log('v2');");
            File.SetLastWriteTimeUtc(editedFile, DateTime.UtcNow.AddMinutes(1));

            var buildEngine = new MockBuildEngine(_output);
            var secondRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = inputDirectory,
                Outputs = output,
                BuildEngine = buildEngine
            };

            Assert.True(secondRun.Execute());
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// A different Sass can minify the same sources differently, so the runtime selection belongs in the stamp.
    /// </summary>
        /// <remarks>
    /// Each case changes one of the three ways a Sass is chosen. All of them have to invalidate: the pack
    /// item contract, the legacy per-RID properties, and the downloaded version are alternative routes to
    /// the same decision, so covering only one would leave the others free to drift.
    /// </remarks>
    [Theory]
    [InlineData(RuntimeSelection.VersionDownload)]
    [InlineData(RuntimeSelection.RuntimePackItem)]
    [InlineData(RuntimeSelection.LegacyRuntimeProperty)]
    public void SassRunTask_WithChangedRuntimeSelection_ShouldNotSkip(RuntimeSelection changed)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-runtime-stamp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var input = Path.Combine(tempDir, "input.js");
            File.WriteAllText(input, "console.log('input');");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(output, "console.log('output');");
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");

            var firstRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(firstRun.Execute());

            var buildEngine = new MockBuildEngine(_output);

            // RuntimeDirectory stays set and wins over both pack contracts, so the second run still resolves
            // the same real Sass and succeeds - the only thing under test is whether the stamp noticed.
            var afterChange = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = buildEngine
            };

            switch (changed)
            {
                case RuntimeSelection.VersionDownload:
                    afterChange.SassVersionDownload = "1.3.12";
                    break;

                case RuntimeSelection.RuntimePackItem:
                    var pack = new TaskItem("Contoso.Sass.Runtime.custom");
                    pack.SetMetadata("Rid", "win-x64");
                    pack.SetMetadata("RuntimesPath", Path.Combine(tempDir, "custom-runtimes"));
                    pack.SetMetadata("Priority", "50");
                    afterChange.RuntimePacks = [pack];
                    break;

                case RuntimeSelection.LegacyRuntimeProperty:
                    afterChange.SassRuntime_win_x64 = Path.Combine(tempDir, "legacy-runtimes");
                    break;
            }

            Assert.True(afterChange.Execute());
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    public enum RuntimeSelection
    {
        VersionDownload,
        RuntimePackItem,
        LegacyRuntimeProperty
    }

    /// <summary>
    /// Incremental skipping is opt-in: half the contract is not enough to start stamping, or a step that
    /// named only its inputs would silently stop running.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SassRunTask_WithOnlyInputsOrOnlyOutputs_ShouldNeverSkip(bool setInputs, bool setOutputs)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-half-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var input = Path.Combine(tempDir, "input.js");
            File.WriteAllText(input, "console.log('input');");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(output, "console.log('output');");
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");

            SassRunTask CreateTask(MockBuildEngine engine) => new()
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = setInputs ? input : null,
                Outputs = setOutputs ? output : null,
                BuildEngine = engine
            };

            var firstRun = CreateTask(new MockBuildEngine(_output));
            Assert.True(firstRun.Execute());
            Assert.Null(firstRun.StampFilePath);

            var buildEngine = new MockBuildEngine(_output);
            var secondRun = CreateTask(buildEngine);

            Assert.True(secondRun.Execute());
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Where the stamp lands is a contract the targets depend on: they pass <c>$(IntermediateOutputPath)</c>
    /// as <c>StampDirectory</c> and feed <c>StampFilePath</c> into <c>@(FileWrites)</c> so a clean removes it.
    /// </summary>
    [Fact]
    public void SassRunTask_ShouldHonourStampFileAndStampDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-stamp-location-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var input = Path.Combine(tempDir, "input.js");
            File.WriteAllText(input, "console.log('input');");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(output, "console.log('output');");
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");

            var explicitStamp = Path.Combine(tempDir, "custom", "my.stamp");
            var withStampFile = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                StampFile = explicitStamp,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(withStampFile.Execute());
            Assert.Equal(explicitStamp, withStampFile.StampFilePath);
            Assert.True(File.Exists(explicitStamp), $"Expected a stamp at {explicitStamp}.");

            var stampDirectory = Path.Combine(tempDir, "intermediate", "Scarlet.Sass");
            var withStampDirectory = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                StampDirectory = stampDirectory,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(withStampDirectory.Execute());
            Assert.NotNull(withStampDirectory.StampFilePath);
            Assert.Equal(stampDirectory, Path.GetDirectoryName(withStampDirectory.StampFilePath));
            Assert.True(File.Exists(withStampDirectory.StampFilePath));

            // StampFile wins over StampDirectory, which is what lets a step opt out of the shared location.
            var bothSet = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                StampFile = explicitStamp,
                StampDirectory = stampDirectory,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(bothSet.Execute());
            Assert.Equal(explicitStamp, bothSet.StampFilePath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SassRunTask_WithProjectDirectory_ShouldResolveIncrementalPathsAgainstProjectDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-project-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var projectDirectory = Path.Combine(tempDir, "project");
            var workingDirectory = Path.Combine(projectDirectory, "frontend");
            Directory.CreateDirectory(workingDirectory);

            File.WriteAllText(Path.Combine(projectDirectory, "input.js"), "console.log('input');");
            File.WriteAllText(Path.Combine(projectDirectory, "Sassdle.js"), "console.log('output');");

            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            const string relativeStampFile = "custom/task.stamp";
            var expectedStampFile = Path.Combine(projectDirectory, "custom", "task.stamp");

            SassRunTask CreateTask(MockBuildEngine engine) => new()
            {
                Command = "--version",
                WorkingDirectory = workingDirectory,
                ProjectDirectory = projectDirectory,
                RuntimeDirectory = runtimesDirectory,
                Inputs = "input.js",
                Outputs = "Sassdle.js",
                StampFile = relativeStampFile,
                BuildEngine = engine
            };

            var firstRun = CreateTask(new MockBuildEngine(_output));
            Assert.True(firstRun.Execute());
            Assert.Equal(expectedStampFile, firstRun.StampFilePath);
            Assert.True(File.Exists(expectedStampFile), $"Expected relative StampFile to resolve under {projectDirectory}.");

            var buildEngine = new MockBuildEngine(_output);
            var secondRun = CreateTask(buildEngine);

            Assert.True(secondRun.Execute());
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Streamed stderr is logged at high importance, which quiet verbosity drops, so the failure message
    /// itself has to carry enough detail to act on even when output is not retained.
    /// </summary>
        /// <remarks>
    /// The retained tail has three shapes that behave differently: a short one reproduced verbatim, one long
    /// enough to be trimmed (which has to say so, or the message silently misrepresents the failure), and an
    /// empty one, where there is nothing to add and the exit code has to stand alone rather than an empty
    /// "Error output:" line.
    /// </remarks>
    [Theory]
    [InlineData(1, false)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    public void SassRunTask_WithCaptureOutputDisabled_ShouldStillReportStderrInTheError(int stderrLineCount, bool expectTruncationNotice)
    {
        using var workspace = TestAssetWorkspace.Create(_output);

        File.WriteAllText(
            Path.Combine(workspace.RootDirectory, "fail.mjs"),
            $$"""
            console.log("STDOUT_CONTEXT");

            for (let i = 1; i <= {{stderrLineCount}}; i++) {
                console.error(`DETAILED_Sass_DIAGNOSTIC line ${i}`);
            }

            process.exit(3);
            """);

        var buildEngine = new MockBuildEngine(_output);
        var task = new SassRunTask
        {
            Command = "run",
            Arguments = "fail.mjs",
            WorkingDirectory = workspace.RootDirectory,
            RuntimeDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes"),
            CaptureOutput = false,
            ContinueOnError = true,
            BuildEngine = buildEngine
        };

        Assert.True(task.Execute());
        Assert.Null(task.StandardError);
        Assert.Contains(buildEngine.Errors, error => error.Message?.Contains("failed with exit code 3", StringComparison.Ordinal) == true);

        // stdout is logged at Normal importance, which the default minimal verbosity drops, so a tool that
        // explains itself there would otherwise leave nothing behind on a failure.
        Assert.Contains(
            buildEngine.Errors,
            error => error.Message?.Contains("Standard output: STDOUT_CONTEXT", StringComparison.Ordinal) == true);

        var errorOutput = buildEngine.Errors
            .Select(error => error.Message)
            .FirstOrDefault(message => message?.StartsWith("Error output:", StringComparison.Ordinal) == true);

        if (stderrLineCount == 0)
        {
            Assert.Null(errorOutput);
            return;
        }

        Assert.NotNull(errorOutput);
        Assert.Contains($"DETAILED_Sass_DIAGNOSTIC line {stderrLineCount}", errorOutput, StringComparison.Ordinal);
        Assert.Equal(expectTruncationNotice, errorOutput!.Contains("(last 50 lines)", StringComparison.Ordinal));

        if (expectTruncationNotice)
        {
            // The dropped head must really be gone, not merely unmentioned.
            Assert.DoesNotContain("DETAILED_Sass_DIAGNOSTIC line 1\n", errorOutput, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SassRunTask_WithMissingIncrementalOutput_ShouldNotSkip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-missing-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(output, "console.log('output');");

            var firstRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(firstRun.Execute());
            File.Delete(output);

            var buildEngine = new MockBuildEngine(_output);
            var secondRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = buildEngine
            };

            // Asserted as "it resolved the runtime and ran" rather than "it failed against a bogus runtime
            // directory": RuntimeDirectory is part of the stamp, so a bogus one would invalidate the stamp
            // and the test would pass for the wrong reason.
            Assert.True(secondRun.Execute());
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("Using Sass at", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SassRunTask_WithInputNewerThanStamp_ShouldNotSkip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-stale-stamp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(output, "console.log('output');");

            var firstRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(firstRun.Execute());
            File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(1));

            var buildEngine = new MockBuildEngine(_output);
            var secondRun = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = buildEngine
            };

            // See SassRunTask_WithMissingIncrementalOutput_ShouldNotSkip: the run is asserted to have happened,
            // not to have failed, so the stamp's own inputs stay identical between the two runs.
            Assert.True(secondRun.Execute());
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("Using Sass at", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SassRunTask_WithCaptureOutputDisabled_ShouldNotRetainStdoutOrStderr()
    {
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var task = new SassRunTask
        {
            Command = "--version",
            RuntimeDirectory = runtimesDirectory,
            CaptureOutput = false,
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal(0, task.ExitCode);
        Assert.Null(task.StandardOutput);
        Assert.Null(task.StandardError);
    }

    [Fact]
    public void SassRunTask_WithFailedIncrementalRun_ShouldNotCreateSuccessStamp()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-failed-incremental-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(output, "partial output");

            var failedRun = new SassRunTask
            {
                Command = "invalid-command-that-does-not-exist-xyz123",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                ContinueOnError = true,
                Inputs = input,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(failedRun.Execute());
            Assert.NotEqual(0, failedRun.ExitCode);

            var buildEngine = new MockBuildEngine(_output);
            var nextRun = new SassRunTask
            {
                Command = "invalid-command-that-does-not-exist-xyz123",
                WorkingDirectory = tempDir,
                RuntimeDirectory = "invalid-runtime-directory",
                Inputs = input,
                Outputs = output,
                BuildEngine = buildEngine
            };

            Assert.False(nextRun.Execute());
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SassRunTask_WhenIncrementalStampCannotBeWritten_ShouldKeepSuccessfulSassResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-Sass-stamp-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var output = Path.Combine(tempDir, "Sassdle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(output, "existing output");

            var buildEngine = new MockBuildEngine(_output);
            var task = new SassRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                StampFile = "invalid\0stamp",
                BuildEngine = buildEngine
            };

            var result = task.Execute();

            Assert.True(result);
            Assert.Equal(0, task.ExitCode);
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("Could not write Sass incremental stamp", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SassRunTask_WithMissingCommand_ShouldFail()
    {
        // Arrange
        var task = new SassRunTask
        {
            Command = null,
            BuildEngine = new MockBuildEngine(_output)
        };

        // Act
        var result = task.Execute();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void SassRunTask_WithRuntimeDownloadAndInvalidRuntimePath_ShouldHitDownloadCatch()
    {
        // Forces SassDownloader to throw before any network call (invalid path chars),
        // which is handled by SassRunTask's runtime-download catch block.
        var task = new SassRunTask
        {
            Command = "run",
            SassRuntimeDownload = true,
            RuntimeDirectory = "invalid\0path",
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void SassRunTask_WithoutRuntimeDirectory_UsesPlatformRuntimeProperty()
    {
        // The legacy SassRuntime_<rid> contract is frozen at the six original platforms (see
        // SassRuntimeResolver.PlatformMap and AGENTS.md) - musl was added afterwards and deliberately never
        // got one, so this legacy-path test has nothing to exercise when it runs on a musl host.
        var currentRid = SassRuntimeResolver.GetRuntimeIdentifier(SassRuntimeResolver.GetCurrentPlatform());
        if (currentRid is "linux-musl-x64" or "linux-musl-arm64")
        {
            return;
        }

        var runtimePackagePath = Directory.GetCurrentDirectory();
        var task = new SassRunTask
        {
            Command = "run",
            RuntimeDirectory = null,
            ContinueOnError = true,
            BuildEngine = new MockBuildEngine(_output)
        };

        SetCurrentPlatformRuntimePath(task, runtimePackagePath);

        var result = task.Execute();

        // ContinueOnError keeps task green even if Sass process exits non-zero.
        Assert.True(result);
        Assert.NotEqual(-1, task.ExitCode);
    }

    [Fact]
    public void SassRunTask_WithTimeout_ShouldKillProcessAndFail()
    {
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var task = new SassRunTask
        {
            Command = "run",
            Arguments = "build.mjs",
            RuntimeDirectory = runtimesDirectory,
            TimeoutMilliseconds = 50,
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void SassRunTask_WithInvalidWorkingDirectory_ShouldHitOuterCatch()
    {
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var task = new SassRunTask
        {
            Command = "install",
            RuntimeDirectory = runtimesDirectory,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            ContinueOnError = true,
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal(-1, task.ExitCode);
    }

    private static void SetCurrentPlatformRuntimePath(SassRunTask task, string runtimePackagePath)
    {
        var platform = SassRuntimeResolver.GetCurrentPlatform();
        var rid = SassRuntimeResolver.GetRuntimeIdentifier(platform);

        switch (rid)
        {
            case "win-x64":
                task.SassRuntime_win_x64 = runtimePackagePath;
                break;
            case "win-arm64":
                task.SassRuntime_win_arm64 = runtimePackagePath;
                break;
            case "linux-x64":
                task.SassRuntime_linux_x64 = runtimePackagePath;
                break;
            case "linux-arm64":
                task.SassRuntime_linux_arm64 = runtimePackagePath;
                break;
            case "osx-x64":
                task.SassRuntime_osx_x64 = runtimePackagePath;
                break;
            case "osx-arm64":
                task.SassRuntime_osx_arm64 = runtimePackagePath;
                break;
            default:
                throw new InvalidOperationException($"Unsupported runtime identifier: {rid}");
        }
    }
}
