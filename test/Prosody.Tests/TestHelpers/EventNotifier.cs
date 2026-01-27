namespace Prosody.Tests.TestHelpers;

/// <summary>
/// One-time event signaling for test coordination. NO SLEEPS.
/// Uses TaskCompletionSource for efficient async waiting.
/// </summary>
/// <remarks>
/// Reference: ../prosody-rb/spec/support/test_config.rb (EventNotifier class)
///
/// Constitution Principle IV: Never use sleep in tests. Use channel-based waiting with timeout.
/// </remarks>
public sealed class EventNotifier
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Signal that the event occurred.
    /// Multiple calls are safe - only the first signal is recorded.
    /// </summary>
    public void Signal() => _tcs.TrySetResult();

    /// <summary>
    /// Signal that the event failed with an exception.
    /// </summary>
    /// <param name="exception">The exception to signal.</param>
    public void SignalError(Exception exception) => _tcs.TrySetException(exception);

    /// <summary>
    /// Wait for the event with cancellation support.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for timeout.</param>
    /// <returns>A task that completes when the event is signaled.</returns>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
        return _tcs.Task;
    }

    /// <summary>
    /// Gets whether the event has been signaled.
    /// </summary>
    public bool IsSignaled => _tcs.Task.IsCompleted;

    /// <summary>
    /// Gets the task representing the event.
    /// </summary>
    public Task Task => _tcs.Task;
}
