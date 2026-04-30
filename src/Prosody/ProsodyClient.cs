using System.Text.Json;
using System.Text.Json.Serialization;
using Prosody.Configuration;
using Prosody.Infrastructure;
using Prosody.Messaging;

namespace Prosody;

/// <summary>
/// Main client for interacting with the Prosody messaging system.
/// </summary>
public sealed class ProsodyClient : IDisposable, IAsyncDisposable
{
    private readonly Native.ProsodyClient _native;

    private ProsodyClient(Native.ProsodyClient native)
    {
        _native = native;
        SourceSystem = native.SourceSystem();
    }

    /// <summary>
    /// Creates a new ProsodyClient with the given options.
    /// </summary>
    /// <param name="options">Configuration options for the client.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="options"/> fails validation.</exception>
    public ProsodyClient(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _native = new Native.ProsodyClient(options.ToNative());
        SourceSystem = _native.SourceSystem();
    }

    /// <summary>
    /// Creates a new ProsodyClient from pre-validated options, skipping redundant validation.
    /// </summary>
    internal static ProsodyClient FromValidatedOptions(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProsodyClient(new Native.ProsodyClient(options.ToNative()));
    }

    /// <summary>
    /// Gets the source system identifier configured for this client.
    /// </summary>
    public string SourceSystem { get; }

    /// <summary>
    /// Gets the current consumer state.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the consumer configuration failed during build, with the full error message.
    /// </exception>
    public async Task<ConsumerState> GetConsumerStateAsync()
    {
        Native.ConsumerState state = await _native.ConsumerState();
        return state switch
        {
            Native.ConsumerState.Unconfigured => ConsumerState.Unconfigured,
            Native.ConsumerState.Configured => ConsumerState.Configured,
            Native.ConsumerState.Running => ConsumerState.Running,
            Native.ConsumerState.ConfigurationFailed failed => throw new InvalidOperationException(
                $"Consumer configuration failed: {failed.Message}"
            ),
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
    /// <remarks>
    /// If <typeparamref name="T"/> exposes lowercase <c>id</c> or <c>type</c> string
    /// properties (matched by <see cref="JsonPropertyNameAttribute"/> or by exact CLR
    /// name), their values are forwarded as event metadata so the producer's idempotence
    /// dedup and downstream <c>allowed_events</c> filtering see them without re-parsing
    /// the JSON. PascalCase properties (<c>Id</c>, <c>Type</c>) must use
    /// <c>[JsonPropertyName("id")]</c> to participate, matching the lowercase wire
    /// contract the rest of the system requires.
    /// </remarks>
    public async Task SendAsync<T>(string topic, string key, T payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        var (eventId, eventType) = TypedEventMetadataExtractor<T>.Extract(payload);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        var carrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        TracePropagation.Inject(carrier);

        var metadata = new Native.EventMetadata(EventId: eventId, EventType: eventType);

        LinkedCancellationSignal? linked = CancellationHelper.CreateSignal(cancellationToken);
        try
        {
            await _native.Send(topic, key, metadata, jsonBytes, carrier, linked?.Signal).ConfigureAwait(false);
        }
        finally
        {
            if (linked is { } l)
            {
                await l.Registration.DisposeAsync().ConfigureAwait(false);
                l.Signal.Dispose();
            }
        }
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
    public Task UnsubscribeAsync() => _native.Unsubscribe();

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
