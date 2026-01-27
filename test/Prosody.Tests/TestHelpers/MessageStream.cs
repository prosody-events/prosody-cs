using System.Collections.Concurrent;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Thread-safe message collection with blocking wait. NO SLEEPS.
/// Uses BlockingCollection for proper synchronization without polling.
/// </summary>
/// <remarks>
/// Reference: ../prosody-rb/spec/support/test_config.rb (MessageStream class)
///
/// Constitution Principle IV: Never use sleep in tests. Use channel-based waiting with timeout.
/// </remarks>
public sealed class MessageStream : IDisposable
{
    private readonly BlockingCollection<Message> _messages = new();
    private bool _disposed;

    /// <summary>
    /// Push a message to the stream (called from handler).
    /// </summary>
    /// <param name="message">The message to push.</param>
    public void Push(Message message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _messages.Add(message);
    }

    /// <summary>
    /// Wait for exactly <paramref name="count"/> messages with timeout.
    /// Uses BlockingCollection.TryTake() - blocks until available or timeout.
    /// </summary>
    /// <param name="count">Number of messages to wait for.</param>
    /// <param name="cancellationToken">Cancellation token for timeout.</param>
    /// <returns>The received messages in order.</returns>
    /// <exception cref="TimeoutException">If timeout occurs before all messages arrive.</exception>
    public async Task<IReadOnlyList<Message>> WaitForMessagesAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var messages = new List<Message>(count);

        // Run blocking collection take on thread pool to avoid blocking caller
        await Task.Run(() =>
        {
            for (var i = 0; i < count; i++)
            {
                // TryTake blocks until message available or cancellation
                if (!_messages.TryTake(out var msg, Timeout.Infinite, cancellationToken))
                {
                    throw new TimeoutException($"Timed out waiting for message {i + 1} of {count}");
                }
                messages.Add(msg);
            }
        }, cancellationToken);

        return messages;
    }

    /// <summary>
    /// Wait for a single message with timeout.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for timeout.</param>
    /// <returns>The received message.</returns>
    public async Task<Message> WaitForMessageAsync(CancellationToken cancellationToken = default)
    {
        var messages = await WaitForMessagesAsync(1, cancellationToken);
        return messages[0];
    }

    /// <summary>
    /// Gets the count of messages currently in the stream.
    /// </summary>
    public int Count => _messages.Count;

    /// <summary>
    /// Clears all messages from the stream.
    /// </summary>
    public void Clear()
    {
        while (_messages.TryTake(out _)) { }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _messages.CompleteAdding();
        _messages.Dispose();
    }
}
