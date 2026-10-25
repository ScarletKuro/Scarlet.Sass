using System;
using System.IO;
using System.Threading;
using Scarlet.Sass.Core;

namespace Scarlet.Sass.MSBuild;

/// <summary>
/// Makes compilations of one project take turns, across every build process on the machine.
/// </summary>
/// <remarks>
/// MSBuild runs a target once per project instance, and one project can be built as several instances in the same
/// build - a ProjectReference that passes its own global properties is enough. Each instance then compiles, and two
/// overlapping compilations write the same CSS, both read and rewrite the generated-file manifest and settings stamp
/// that stale-output removal and clean depend on, and after a settings change one deletes outputs the other has
/// just written. The project directory is the key because it holds all of them.
/// </remarks>
internal sealed class SassRunLock : IDisposable
{
    private readonly Mutex _mutex;

    private SassRunLock(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>
    /// Blocks until no other compilation of <paramref name="projectDirectory"/> is running.
    /// </summary>
    /// <param name="onWait">Called with the locked directory when another compilation holds the lock.</param>
    internal static SassRunLock Acquire(string projectDirectory, Action<string> onWait)
    {
        var directory = ResolveDirectory(projectDirectory);
        var mutex = new Mutex(false, CreateMutexName(directory));

        try
        {
            if (!mutex.WaitOne(0))
            {
                onWait(directory);

                // No timeout of its own: the holder is a compilation bounded by its own TimeoutMilliseconds, and a
                // holder that dies abandons the mutex rather than keeping it.
                mutex.WaitOne();
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died mid-compilation. We own the mutex now; the settings stamp it never rewrote
            // makes this compilation regenerate what it may have left half-written.
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        return new SassRunLock(mutex);
    }

    /// <summary>
    /// Releases the lock. Thread-affine, like the download mutex: dispose it on the thread that acquired it.
    /// </summary>
    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    internal static string ResolveDirectory(string projectDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(projectDirectory) ? Directory.GetCurrentDirectory() : projectDirectory;

        return Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static string CreateMutexName(string directory)
    {
        // Upper-cased so that "C:\App" and "c:\app" share a lock on Windows. On a case-sensitive file system two
        // directories differing only in case then share one too, which costs at most some parallelism.
        var hashString = HashUtilities.ComputeSha256Hex(directory.ToUpperInvariant()).ToUpperInvariant();
        return $"Global\\ScarletSass_Run_{hashString}";
    }
}
