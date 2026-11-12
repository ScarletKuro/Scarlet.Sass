using System.Diagnostics.CodeAnalysis;

namespace Scarlet.Sass.Cli.Watch;

internal interface IWatchSessionLockProvider
{
    bool TryAcquire(IReadOnlyList<SassWatchInvocation> invocations, [NotNullWhen(true)] out IDisposable? lease);
}
