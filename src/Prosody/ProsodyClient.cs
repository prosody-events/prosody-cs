using System.Text.Json;

namespace Prosody;

/// <summary>
/// Main client for interacting with the Prosody messaging system.
/// </summary>
/// <remarks>
/// Provides functionality for sending messages, subscribing to topics,
/// and managing consumer state.
/// </remarks>
public sealed class ProsodyClient : IDisposable
{
    private readonly Native.ProsodyClient _native;
    private bool _disposed;

    /// <summary>
    /// Creates a new ProsodyClient with the given options.
    /// </summary>
    /// <param name="options">Configuration options for the client.</param>
    /// <exception cref="ProsodyException">Thrown if the client cannot connect or options are invalid.</exception>
    public ProsodyClient(ClientOptions options)
    {
        _native = new Native.ProsodyClient(options.ToNative());
    }

    /// <summary>
    /// Gets the source system identifier configured for this client.
    /// </summary>
    public string SourceSystem => _native.SourceSystem();

    /// <summary>
    /// Gets the current consumer state.
    /// </summary>
    public ConsumerState ConsumerState => _native.ConsumerState() switch
    {
        Native.ConsumerState.Unconfigured => ConsumerState.Unconfigured,
        Native.ConsumerState.Configured => ConsumerState.Configured,
        Native.ConsumerState.Running => ConsumerState.Running,
        _ => throw new InvalidOperationException("Unknown consumer state")
    };

    /// <summary>
    /// Gets the number of partitions currently assigned to this consumer.
    /// </summary>
    public uint AssignedPartitionCount => _native.AssignedPartitionCount();

    /// <summary>
    /// Gets a value indicating whether the consumer is currently stalled.
    /// </summary>
    public bool IsStalled => _native.IsStalled();

    /// <summary>
    /// Sends a message to a topic.
    /// </summary>
    /// <typeparam name="T">The type of the payload to serialize as JSON.</typeparam>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="payload">The message payload (will be serialized to JSON).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <exception cref="ProsodyException">Thrown if the send operation fails.</exception>
    public Task Send<T>(string topic, string key, T payload, CancellationToken cancellationToken = default)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return SendRaw(topic, key, jsonBytes, cancellationToken);
    }

    /// <summary>
    /// Sends a raw JSON message to a topic.
    /// </summary>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="jsonPayload">The message payload as UTF-8 JSON bytes.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <exception cref="ProsodyException">Thrown if the send operation fails.</exception>
    public Task SendRaw(string topic, string key, byte[] jsonPayload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var carrier = new Dictionary<string, string>();
        TracePropagation.Inject(carrier);
        var signal = CancellationHelper.CreateSignal(cancellationToken);

        return _native.Send(topic, key, jsonPayload, carrier, signal);
    }

    /// <summary>
    /// Subscribes to receive messages using the provided event handler.
    /// </summary>
    /// <param name="handler">The event handler to process messages and timers.</param>
    /// <exception cref="ProsodyException">Thrown if subscription fails.</exception>
    public Task Subscribe(IEventHandler handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bridge = new EventHandlerBridge(handler);
        return _native.Subscribe(bridge);
    }

    /// <summary>
    /// Unsubscribes from receiving messages and shuts down the consumer.
    /// </summary>
    /// <exception cref="ProsodyException">Thrown if unsubscribe fails.</exception>
    public Task Unsubscribe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _native.Unsubscribe();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _native.Dispose();
    }
}
