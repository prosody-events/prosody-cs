namespace Prosody.Tests.TestHelpers;

/// <summary>
/// One-time event signaling for test coordination.
/// Uses TaskCompletionSource for efficient async waiting instead of sleep-based polling.
/// </summary>
internal sealed class EventNotifier
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Signal that the event occurred. Multiple calls are safe; only the first is recorded.
    /// </summary>
    public void Signal() => _tcs.TrySetResult();

    /// <summary>
    /// Signal that the event failed with an exception.
    /// </summary>
    public void SignalError(Exception exception) => _tcs.TrySetException(exception);

    /// <summary>
    /// Wait for the event with optional cancellation/timeout support.
    /// </summary>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
        return _tcs.Task;
    }

    /// <summary>
    /// Whether the event has been signaled.
    /// </summary>
    public bool IsSignaled => _tcs.Task.IsCompleted;

    /// <summary>
    /// The underlying task representing the event.
    /// </summary>
    public Task Task => _tcs.Task;
}
