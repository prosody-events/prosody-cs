using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Infrastructure;
using Prosody.Logging;
using Prosody.Messaging;
using Prosody.State;

namespace Prosody;

/// <summary>
/// Main client for interacting with the Prosody messaging system.
/// </summary>
public sealed class ProsodyClient : IDisposable, IAsyncDisposable
{
    private readonly Native.ProsodyClient _native;
    private readonly IReadOnlySet<StateDefinition> _stateDefinitions;

    internal JsonSerializerOptions JsonOptions { get; }

    private ProsodyClient(
        Native.ProsodyClient native,
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition> stateDefinitions
    )
    {
        _native = native;
        JsonOptions = jsonOptions;
        _stateDefinitions = stateDefinitions;
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
    [RequiresUnreferencedCode(
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to use trim-safe serialization."
    )]
    [RequiresDynamicCode(
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to avoid runtime code generation."
    )]
    public ProsodyClient(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _native = new Native.ProsodyClient(options.ToNative());
        JsonOptions = BuildJsonOptions(options);
        _stateDefinitions = RegisteredStateDefinitions(options);
        SourceSystem = _native.SourceSystem();
    }

    /// <summary>
    /// Creates a new ProsodyClient from pre-validated options, skipping redundant validation.
    /// </summary>
    [RequiresUnreferencedCode(
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to use trim-safe serialization."
    )]
    [RequiresDynamicCode(
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to avoid runtime code generation."
    )]
    internal static ProsodyClient FromValidatedOptions(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProsodyClient(
            new Native.ProsodyClient(options.ToNative()),
            BuildJsonOptions(options),
            RegisteredStateDefinitions(options)
        );
    }

    private static HashSet<StateDefinition> RegisteredStateDefinitions(ClientOptions options) =>
        new HashSet<StateDefinition>(options.StateCollections ?? [], ReferenceEqualityComparer.Instance);

    [RequiresUnreferencedCode(
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to use trim-safe serialization."
    )]
    [RequiresDynamicCode(
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext to avoid runtime code generation."
    )]
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

    /// <summary>Opens a read-only published value collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedValue<T>> StateAsync<T>(
        string subsystem,
        ValueStateDefinition<T> definition,
        CancellationToken cancellationToken = default
    )
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(definition);
        var handle = await StateInterop
            .RunAsync(
                () =>
                    _native.PublishedValue(
                        subsystem,
                        definition.Name,
                        definition.ReadCacheTtl,
                        definition.ReadCacheDisabled
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
        return new PublishedValue<T>(handle, StateInterop.ResolveTypeInfo<T>(JsonOptions));
    }

    /// <summary>Opens a read-only published map collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedMap<TValue>> StateAsync<TValue>(
        string subsystem,
        MapStateDefinition<TValue> definition,
        CancellationToken cancellationToken = default
    )
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(definition);
        var handle = await StateInterop
            .RunAsync(
                () =>
                    _native.PublishedMap(
                        subsystem,
                        definition.Name,
                        definition.ReadCacheTtl,
                        definition.ReadCacheDisabled
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
        return new PublishedMap<TValue>(handle, StateInterop.ResolveTypeInfo<TValue>(JsonOptions));
    }

    /// <summary>Opens a read-only published deque collection from the same descriptor used by its owner.</summary>
    public async Task<PublishedDeque<T>> StateAsync<T>(
        string subsystem,
        DequeStateDefinition<T> definition,
        CancellationToken cancellationToken = default
    )
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        ArgumentNullException.ThrowIfNull(definition);
        var handle = await StateInterop
            .RunAsync(
                () =>
                    _native.PublishedDeque(
                        subsystem,
                        definition.Name,
                        definition.ReadCacheTtl,
                        definition.ReadCacheDisabled
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
        return new PublishedDeque<T>(handle, StateInterop.ResolveTypeInfo<T>(JsonOptions));
    }

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
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before or during the send.</exception>
    [RequiresUnreferencedCode(
        "Resolves JsonTypeInfo<T> from the client's options resolver, which may use DefaultJsonTypeInfoResolver (reflection-based). Use the SendAsync overload that accepts JsonTypeInfo<T> for trim-safe publishing."
    )]
    [RequiresDynamicCode(
        "Resolves JsonTypeInfo<T> from the client's options resolver, which may use DefaultJsonTypeInfoResolver. Use the SendAsync overload that accepts JsonTypeInfo<T> for trim-safe publishing."
    )]
    public Task SendAsync<T>(string topic, string key, T payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        var typeInfo = (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));
        return SendCoreAsync(topic, key, payload, typeInfo, null, cancellationToken);
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
    /// <remarks>
    /// <para>
    /// Event metadata (<c>id</c> and <c>type</c>) is extracted by walking the
    /// <paramref name="typeInfo"/>'s property list. If your source-generated context
    /// uses a naming policy that does not produce lowercase <c>"id"</c>/<c>"type"</c>
    /// property names, extraction will silently yield <c>null</c>. In that scenario,
    /// use the overload that accepts <see cref="SendOptions"/> to provide explicit
    /// <see cref="SendOptions.EventId"/> and <see cref="SendOptions.EventType"/> values.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before or during the send.</exception>
    public Task SendAsync<T>(
        string topic,
        string key,
        T payload,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(typeInfo);
        cancellationToken.ThrowIfCancellationRequested();

        return SendCoreAsync(topic, key, payload, typeInfo, null, cancellationToken);
    }

    /// <summary>
    /// Sends a message to a topic, serializing <paramref name="payload"/> using the supplied
    /// <paramref name="typeInfo"/> with explicit metadata overrides (trim-safe).
    /// </summary>
    /// <typeparam name="T">The type of the payload to serialize as JSON.</typeparam>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="key">The message key.</param>
    /// <param name="payload">The message payload (will be serialized to JSON).</param>
    /// <param name="typeInfo">
    /// Source-generated <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/>.
    /// </param>
    /// <param name="options">
    /// Per-message overrides. When <see cref="SendOptions.EventId"/> or
    /// <see cref="SendOptions.EventType"/> is set, that value is used instead of
    /// extracting from the payload.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before or during the send.</exception>
    public Task SendAsync<T>(
        string topic,
        string key,
        T payload,
        JsonTypeInfo<T> typeInfo,
        SendOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return SendCoreAsync(topic, key, payload, typeInfo, options, cancellationToken);
    }

    private async Task SendCoreAsync<T>(
        string topic,
        string key,
        T payload,
        JsonTypeInfo<T> typeInfo,
        SendOptions? options,
        CancellationToken cancellationToken
    )
    {
        var (extractedId, extractedType) = TypedEventMetadataExtractor.Extract(payload, typeInfo);
        var eventId = options?.EventId ?? extractedId;
        var eventType = options?.EventType ?? extractedType;
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
        // Invariant: the signal is triggered only by cancellationToken's registration
        // (CancellationHelper.CreateSignal), so a native Cancelled from the send path always
        // means the caller's token fired — surface it as the standard .NET cancellation type.
        catch (Native.FfiException.Cancelled ex)
        {
            throw new OperationCanceledException("The send was cancelled.", ex, cancellationToken);
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
    /// This overload reads <c>PermanentErrorAttribute</c> from handler methods via
    /// <c>Type.GetInterfaceMap</c> (BCL-annotated and AOT-compatible at the BCL level).
    /// It is annotated with <c>[RequiresUnreferencedCode]</c>/<c>[RequiresDynamicCode]</c>
    /// because the trimmer cannot propagate DAM requirements through an interface-typed
    /// parameter to satisfy call-site annotation requirements. Use the
    /// <c>SubscribeAsync&lt;TPayload&gt;(IProsodyHandler&lt;TPayload&gt;, IPermanentErrorClassifier)</c>
    /// overload for explicit, zero-reflection error classification.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(
        "Reads PermanentErrorAttribute from handler methods via reflection. Use SubscribeAsync(handler, classifier) to avoid the reflection path."
    )]
    [RequiresDynamicCode(
        "GetInterfaceMap requires handler type methods to be preserved. Use SubscribeAsync(handler, classifier) to avoid this requirement."
    )]
    public Task SubscribeAsync<TPayload>(IProsodyHandler<TPayload> handler)
    {
        var bridge = new EventHandlerBridge<TPayload>(handler, JsonOptions, _stateDefinitions);
        return _native.Subscribe(bridge);
    }

    /// <summary>
    /// Subscribes to receive messages using the provided strongly typed event handler and
    /// an explicit error classifier (zero reflection; no attribute lookup is performed).
    /// </summary>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="handler">The event handler to process messages and timers.</param>
    /// <param name="classifier">
    /// Classifies exceptions thrown by <paramref name="handler"/> as permanent or transient.
    /// Bypasses the reflection-based <c>PermanentErrorAttribute</c> lookup entirely.
    /// </param>
    /// <remarks>
    /// Use this overload when you want full control over error classification or want to avoid
    /// the reflection path entirely. Pair with a source-generated <c>JsonSerializerContext</c>
    /// (via <see cref="ClientOptions.ConfigureJsonOptions"/>) when building for a fully
    /// zero-reflection payload deserialization path as well.
    /// </remarks>
    public Task SubscribeAsync<TPayload>(IProsodyHandler<TPayload> handler, IPermanentErrorClassifier classifier)
    {
        var bridge = new EventHandlerBridge<TPayload>(handler, JsonOptions, classifier, _stateDefinitions);
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

        // Flush this client's final telemetry (including the unsubscribe span above)
        // before native teardown so a promptly-exiting process does not lose it.
        // Flush, not shutdown: telemetry is process-global and sibling clients may
        // still be running. Best-effort — a flush failure must not fault disposal.
        try
        {
            ProsodyLogging.FlushTelemetry();
        }
        catch (Native.FfiException)
        {
            // Ignore - telemetry flush is best-effort during disposal
        }

        _native.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose() => _native.Dispose();
}
