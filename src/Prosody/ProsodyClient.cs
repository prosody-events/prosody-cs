namespace Prosody;

/// <summary>
/// High-level client for interacting with Kafka using the Prosody library.
/// </summary>
/// <remarks>
/// This client provides async/await-friendly methods for producing and consuming
/// Kafka messages with built-in support for OpenTelemetry distributed tracing.
/// </remarks>
public sealed class ProsodyClient : IAsyncDisposable
{
    private readonly ProsodyClientOptions _options;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProsodyClient"/> class.
    /// </summary>
    /// <param name="options">The client configuration options.</param>
    public ProsodyClient(ProsodyClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Sends a message to the specified topic.
    /// </summary>
    /// <param name="topic">The target topic.</param>
    /// <param name="key">The message key for partitioning.</param>
    /// <param name="payload">The message payload.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the message is sent.</returns>
    public Task SendAsync(string topic, string key, object payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Subscribes to messages using the specified event handler.
    /// </summary>
    /// <param name="handler">The handler to process incoming messages and timers.</param>
    /// <param name="cancellationToken">A token to cancel the subscription.</param>
    /// <returns>A task that completes when the subscription is established.</returns>
    public Task SubscribeAsync(IEventHandler handler, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Unsubscribes from the current subscription.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when unsubscribed.</returns>
    public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // TODO: Dispose native resources
        await Task.CompletedTask;
    }
}
