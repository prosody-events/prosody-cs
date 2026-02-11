using System.Text.Json;

namespace Prosody;

/// <summary>
/// Main client for interacting with the Prosody messaging system.
/// </summary>
public sealed class ProsodyClient : IDisposable, IAsyncDisposable
{
    private readonly Native.ProsodyClient _native;

    /// <summary>
    /// Creates a new ProsodyClient with the given options.
    /// </summary>
    /// <param name="options">Configuration options for the client.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ProsodyClient(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _native = new Native.ProsodyClient(options.ToNative());
    }

    /// <summary>
    /// Gets the source system identifier configured for this client.
    /// </summary>
    public string SourceSystem => _native.SourceSystem();

    /// <summary>
    /// Gets the current consumer state.
    /// </summary>
    public async Task<ConsumerState> ConsumerStateAsync()
    {
        return await _native.ConsumerState() switch
        {
            Native.ConsumerState.Unconfigured => ConsumerState.Unconfigured,
            Native.ConsumerState.Configured => ConsumerState.Configured,
            Native.ConsumerState.Running => ConsumerState.Running,
            _ => throw new InvalidOperationException("Unknown consumer state"),
        };
    }

    /// <summary>
    /// Gets the number of partitions currently assigned to this consumer.
    /// </summary>
    public Task<uint> AssignedPartitionCountAsync() => _native.AssignedPartitionCount();

    /// <summary>
    /// Gets a value indicating whether the consumer is currently stalled.
    /// </summary>
    public Task<bool> IsStalledAsync() => _native.IsStalled();

    /// <summary>
    /// Sends a message to a topic.
    /// </summary>
    /// <typeparam name="T">The type of the payload to serialize as JSON.</typeparam>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="payload">The message payload (will be serialized to JSON).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public Task SendAsync<T>(
        string topic,
        string key,
        T payload,
        CancellationToken cancellationToken = default
    )
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return SendRawAsync(topic, key, jsonBytes, cancellationToken);
    }

    /// <summary>
    /// Sends a raw JSON message to a topic.
    /// </summary>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="jsonPayload">The message payload as UTF-8 JSON bytes.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task SendRawAsync(
        string topic,
        string key,
        byte[] jsonPayload,
        CancellationToken cancellationToken = default
    )
    {
        var carrier = new Dictionary<string, string>();
        TracePropagation.Inject(carrier);
        using var signal = CancellationHelper.CreateSignal(cancellationToken);

        await _native.Send(topic, key, jsonPayload, carrier, signal).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to receive messages using the provided event handler.
    /// </summary>
    /// <param name="handler">The event handler to process messages and timers.</param>
    public Task SubscribeAsync(IProsodyHandler handler)
    {
        var bridge = new EventHandlerBridge(handler);
        return _native.Subscribe(bridge);
    }

    /// <summary>
    /// Unsubscribes from receiving messages and shuts down the consumer.
    /// </summary>
    public Task UnsubscribeAsync()
    {
        return _native.Unsubscribe();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _native.Unsubscribe().ConfigureAwait(false);
        }
        catch (Native.FfiException.Client)
        {
            // Ignore - consumer was not running or already unsubscribed
        }
        catch (ObjectDisposedException)
        {
            // Ignore - already disposed
        }

        _native.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose() => _native.Dispose();
}
