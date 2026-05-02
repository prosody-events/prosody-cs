using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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

    internal JsonSerializerOptions JsonOptions { get; }

    private ProsodyClient(Native.ProsodyClient native, JsonSerializerOptions jsonOptions)
    {
        _native = native;
        JsonOptions = jsonOptions;
        SourceSystem = native.SourceSystem();
    }

    /// <summary>
    /// Creates a new ProsodyClient with the given options.
    /// </summary>
    /// <param name="options">Configuration options for the client.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="options"/> fails validation.</exception>
    /// <remarks>
    /// When no <c>TypeInfoResolver</c> is set via <see cref="ClientOptions.ConfigureJsonOptions"/>,
    /// this constructor auto-installs <c>DefaultJsonTypeInfoResolver</c>, which uses reflection metadata.
    /// To avoid this, set <c>TypeInfoResolver</c> to a source-generated <c>JsonSerializerContext</c>
    /// in the <see cref="ClientOptions.ConfigureJsonOptions"/> callback.
    /// </remarks>
    [RequiresUnreferencedCode("Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to use trim-safe serialization.")]
    [RequiresDynamicCode("Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to avoid runtime code generation.")]
    public ProsodyClient(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _native = new Native.ProsodyClient(options.ToNative());
        JsonOptions = BuildJsonOptions(options);
        SourceSystem = _native.SourceSystem();
    }

    /// <summary>
    /// Creates a new ProsodyClient from pre-validated options, skipping redundant validation.
    /// </summary>
    [RequiresUnreferencedCode("Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to use trim-safe serialization.")]
    [RequiresDynamicCode("Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to avoid runtime code generation.")]
    internal static ProsodyClient FromValidatedOptions(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProsodyClient(new Native.ProsodyClient(options.ToNative()), BuildJsonOptions(options));
    }

    [RequiresUnreferencedCode("Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to use trim-safe serialization.")]
    [RequiresDynamicCode("Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to avoid runtime code generation.")]
    private static JsonSerializerOptions BuildJsonOptions(ClientOptions options)
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        options.ConfigureJsonOptions?.Invoke(opts);
        opts.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        opts.MakeReadOnly();
        return opts;
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
    /// Sends a message to a topic, serializing <paramref name="payload"/> with the client's
    /// configured <see cref="JsonSerializerOptions"/>.
    /// </summary>
    /// <typeparam name="T">The type of the payload to serialize as JSON.</typeparam>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="payload">The message payload (will be serialized to JSON).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <remarks>
    /// <para>
    /// Resolves <see cref="JsonTypeInfo{T}"/> from the client's configured options. If those
    /// options use <c>DefaultJsonTypeInfoResolver</c> (the default when no resolver is set via
    /// <see cref="ClientOptions.ConfigureJsonOptions"/>), this call uses reflection metadata.
    /// For trim-safe publishing, use the <c>SendAsync&lt;T&gt;(string, string, T, JsonTypeInfo&lt;T&gt;, CancellationToken)</c>
    /// overload and pass a source-generated <see cref="JsonTypeInfo{T}"/> directly.
    /// </para>
    /// <para>
    /// If <typeparamref name="T"/> exposes lowercase <c>id</c> or <c>type</c> string
    /// properties (matched by <see cref="JsonPropertyNameAttribute"/> or by exact CLR
    /// name), their values are forwarded as event metadata so the producer's idempotence
    /// dedup and downstream <c>allowed_events</c> filtering see them without re-parsing
    /// the JSON. PascalCase properties (<c>Id</c>, <c>Type</c>) must use
    /// <c>[JsonPropertyName("id")]</c> to participate, matching the lowercase wire
    /// contract the rest of the system requires.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Resolves JsonTypeInfo<T> from the client's options resolver, which may use DefaultJsonTypeInfoResolver (reflection-based). Use the SendAsync overload that accepts JsonTypeInfo<T> for trim-safe publishing.")]
    [RequiresDynamicCode("Resolves JsonTypeInfo<T> from the client's options resolver, which may use DefaultJsonTypeInfoResolver. Use the SendAsync overload that accepts JsonTypeInfo<T> for trim-safe publishing.")]
    public Task SendAsync<T>(string topic, string key, T payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        var typeInfo = (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));
        return SendCoreAsync(topic, key, payload, typeInfo, cancellationToken);
    }

    /// <summary>
    /// Sends a message to a topic, serializing <paramref name="payload"/> using the supplied
    /// <paramref name="typeInfo"/> (trim-safe; no reflection resolver is consulted).
    /// </summary>
    /// <typeparam name="T">The type of the payload to serialize as JSON.</typeparam>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="payload">The message payload (will be serialized to JSON).</param>
    /// <param name="typeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/>.
    /// Use a source-generated <c>JsonSerializerContext</c> to obtain one:
    /// <c>AppJsonContext.Default.MyType</c>.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public Task SendAsync<T>(
        string topic,
        string key,
        T payload,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(typeInfo);
        cancellationToken.ThrowIfCancellationRequested();

        return SendCoreAsync(topic, key, payload, typeInfo, cancellationToken);
    }

    private async Task SendCoreAsync<T>(string topic, string key, T payload, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        var (eventId, eventType) = TypedEventMetadataExtractor.Extract(payload, typeInfo);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo);

        // W3C propagation injects at most 2 headers (traceparent, tracestate); pre-size to avoid rehash.
        var carrier = new Dictionary<string, string>(capacity: 2, StringComparer.OrdinalIgnoreCase);
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
    /// Subscribes to receive messages using the provided strongly typed event handler.
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="handler">The event handler to process messages and timers.</param>
    /// <remarks>
    /// <para>
    /// The payload is deserialized once into <see cref="Message{T}.Payload"/> before the
    /// handler is invoked. For topics with dynamic or mixed schemas, use
    /// <c>TPayload = <see cref="System.Text.Json.JsonElement"/></c>.
    /// </para>
    /// <para>
    /// This overload reads <c>PermanentErrorAttribute</c> from handler methods via reflection
    /// and uses <c>Type.GetInterfaceMap</c> to resolve explicit interface implementations —
    /// both are incompatible with trimming and Native AOT. For AOT-safe error classification,
    /// use the <c>SubscribeAsync&lt;TPayload&gt;(IProsodyHandler&lt;TPayload&gt;, IPermanentErrorClassifier)</c>
    /// overload instead.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(
        "Reads PermanentErrorAttribute from handler methods via reflection. Type.GetInterfaceMap is not supported under trimming; use SubscribeAsync(handler, classifier) for AOT-safe error classification."
    )]
    [RequiresDynamicCode("Type.GetInterfaceMap is not supported in Native AOT. Use SubscribeAsync(handler, classifier) for AOT-safe error classification.")]
    public Task SubscribeAsync<TPayload>(IProsodyHandler<TPayload> handler)
    {
        var bridge = new EventHandlerBridge<TPayload>(handler, JsonOptions);
        return _native.Subscribe(bridge);
    }

    /// <summary>
    /// Subscribes to receive messages using the provided strongly typed event handler and
    /// an explicit error classifier (trim-safe; no reflection is used).
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="handler">The event handler to process messages and timers.</param>
    /// <param name="classifier">
    /// Classifies exceptions thrown by <paramref name="handler"/> as permanent or transient.
    /// Bypasses the reflection-based <c>PermanentErrorAttribute</c> lookup entirely.
    /// </param>
    /// <remarks>
    /// Use this overload together with a source-generated <c>JsonSerializerContext</c> (via
    /// <see cref="ClientOptions.ConfigureJsonOptions"/>) for a fully AOT-safe subscribe path.
    /// </remarks>
    public Task SubscribeAsync<TPayload>(IProsodyHandler<TPayload> handler, IPermanentErrorClassifier classifier)
    {
        var bridge = new EventHandlerBridge<TPayload>(handler, JsonOptions, classifier);
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
