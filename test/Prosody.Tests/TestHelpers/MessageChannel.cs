using System.Threading.Channels;

namespace Prosody.Tests.TestHelpers;

/// <summary>
/// Thread-safe channel for collecting messages in tests with timeout-based waiting.
/// </summary>
/// <typeparam name="T">The type of items to collect.</typeparam>
internal sealed class MessageChannel<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }
    );

    /// <summary>Sends an item to the channel.</summary>
    public void Send(T item) => _channel.Writer.TryWrite(item);

    /// <summary>Receives a single item from the channel with timeout.</summary>
    /// <exception cref="TimeoutException">Thrown if no item is received within timeout.</exception>
    public async Task<T> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout waiting for item after {timeout}");
        }
    }

    /// <summary>Receives multiple items from the channel with timeout.</summary>
    /// <exception cref="TimeoutException">Thrown if not all items are received within timeout.</exception>
    public async Task<List<T>> ReceiveAsync(int count, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var items = new List<T>(count);

        try
        {
            for (var i = 0; i < count; i++)
            {
                items.Add(await _channel.Reader.ReadAsync(cts.Token));
            }
            return items;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout waiting for {count} items after {timeout}. Received {items.Count}.");
        }
    }

    /// <summary>Tries to receive an item without waiting.</summary>
    public bool TryReceive(out T? item) => _channel.Reader.TryRead(out item);

    /// <summary>Gets the number of items currently in the channel.</summary>
    public int Count => _channel.Reader.Count;
}
