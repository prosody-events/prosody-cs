using System.Collections.Concurrent;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Thread-safe timer event collection with blocking wait. NO SLEEPS.
/// Uses BlockingCollection for proper synchronization without polling.
/// </summary>
/// <remarks>
/// Reference: ../prosody-rb/spec/support/test_config.rb (TimerEventStream class)
///
/// Constitution Principle IV: Never use sleep in tests. Use channel-based waiting with timeout.
/// </remarks>
public sealed class TimerEventStream : IDisposable
{
    private readonly BlockingCollection<Trigger> _timers = new();
    private bool _disposed;

    /// <summary>
    /// Push a timer event to the stream (called from handler).
    /// </summary>
    /// <param name="timer">The timer event to push.</param>
    public void Push(Trigger timer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timers.Add(timer);
    }

    /// <summary>
    /// Wait for exactly <paramref name="count"/> timer events with timeout.
    /// Uses BlockingCollection.TryTake() - blocks until available or timeout.
    /// </summary>
    /// <param name="count">Number of timer events to wait for.</param>
    /// <param name="cancellationToken">Cancellation token for timeout.</param>
    /// <returns>The received timer events in order.</returns>
    /// <exception cref="TimeoutException">If timeout occurs before all timers arrive.</exception>
    public async Task<IReadOnlyList<Trigger>> WaitForTimersAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var timers = new List<Trigger>(count);

        // Run blocking collection take on thread pool to avoid blocking caller
        await Task.Run(() =>
        {
            for (var i = 0; i < count; i++)
            {
                // TryTake blocks until timer available or cancellation
                if (!_timers.TryTake(out var timer, Timeout.Infinite, cancellationToken))
                {
                    throw new TimeoutException($"Timed out waiting for timer {i + 1} of {count}");
                }
                timers.Add(timer);
            }
        }, cancellationToken);

        return timers;
    }

    /// <summary>
    /// Wait for a single timer event with timeout.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for timeout.</param>
    /// <returns>The received timer event.</returns>
    public async Task<Trigger> WaitForTimerAsync(CancellationToken cancellationToken = default)
    {
        var timers = await WaitForTimersAsync(1, cancellationToken);
        return timers[0];
    }

    /// <summary>
    /// Gets the count of timer events currently in the stream.
    /// </summary>
    public int Count => _timers.Count;

    /// <summary>
    /// Clears all timer events from the stream.
    /// </summary>
    public void Clear()
    {
        while (_timers.TryTake(out _)) { }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timers.CompleteAdding();
        _timers.Dispose();
    }
}
