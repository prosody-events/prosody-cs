using Prosody.Infrastructure;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Test double for <see cref="CancelWatch"/>. Captures the registered
/// cancellation action so a test can push a cancel with <see cref="Fire"/>,
/// like the native watcher does.
/// </summary>
internal sealed class FakeCancelWatch
{
    private volatile Action? _onCancelled;

    internal bool Stopped { get; private set; }

    internal CancelWatch Watch => new(Watch: action => _onCancelled = action, Stop: () => Stopped = true);

    /// <summary>Invokes the registered action. Throws when none is registered.</summary>
    internal void Fire()
    {
        Action? onCancelled = _onCancelled;
        Assert.NotNull(onCancelled);
        onCancelled();
    }
}
