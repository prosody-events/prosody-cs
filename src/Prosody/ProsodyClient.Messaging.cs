using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Prosody.Configuration;
using Prosody.Errors;
using Prosody.Infrastructure;
using Prosody.Messaging;

namespace Prosody;

// Sends messages and excise records, and subscribes handlers to the configured topics.
public sealed partial class ProsodyClient
{
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
    [RequiresUnreferencedCode(Trimming.JsonMetadata)]
    [RequiresDynamicCode(Trimming.JsonMetadata)]
    public Task SendAsync<T>(string topic, string key, T payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        var typeInfo = (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));
        return SendCoreAsync(topic, key, payload, typeInfo, null, cancellationToken);
    }

    /// <summary>Sends an excise record for a key.</summary>
    public async Task ExciseAsync(string topic, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        var carrier = new Dictionary<string, string>(capacity: 2, StringComparer.OrdinalIgnoreCase);
        TracePropagation.Inject(carrier);
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
        LinkedCancellationSignal? linked = CancellationHelper.CreateSignal(cancellationToken);
        try
        {
            await native.Excise(topic, key, carrier, linked?.Signal).ConfigureAwait(false);
        }
        catch (Native.FfiException.Cancelled ex)
        {
            throw new OperationCanceledException("The excise was cancelled.", ex, cancellationToken);
        }
        finally
        {
            if (linked is { } value)
            {
                await value.Registration.DisposeAsync().ConfigureAwait(false);
                value.Signal.Dispose();
            }
        }
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
        var native = await NativeAsync(cancellationToken).ConfigureAwait(false);
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
            await native.Send(topic, key, metadata, jsonBytes, carrier, linked?.Signal).ConfigureAwait(false);
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
    [RequiresUnreferencedCode(Trimming.HandlerReflection)]
    [RequiresDynamicCode(Trimming.HandlerReflection)]
    public Task SubscribeAsync<TPayload>(IProsodyHandler<TPayload> handler) =>
        SubscribeCoreAsync(new EventHandlerBridge<TPayload>(handler, JsonOptions, _stateDefinitions));

    /// <summary>Subscribes with a handler that returns subsystem responses.</summary>
    [RequiresUnreferencedCode(Trimming.HandlerReflection)]
    [RequiresDynamicCode(Trimming.HandlerReflection)]
    public Task SubscribeAsync<TPayload, TResponse>(IProsodyRequestHandler<TPayload, TResponse> handler) =>
        SubscribeCoreAsync(EventHandlerBridge<TPayload>.Responding(handler, JsonOptions, _stateDefinitions));

    /// <summary>Subscribes with a response handler and an explicit error classifier.</summary>
    /// <remarks>This overload does not inspect <see cref="PermanentErrorAttribute"/>.</remarks>
    public Task SubscribeAsync<TPayload, TResponse>(
        IProsodyRequestHandler<TPayload, TResponse> handler,
        IPermanentErrorClassifier classifier
    ) =>
        SubscribeCoreAsync(
            EventHandlerBridge<TPayload>.Responding(handler, JsonOptions, _stateDefinitions, classifier)
        );

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
    public Task SubscribeAsync<TPayload>(IProsodyHandler<TPayload> handler, IPermanentErrorClassifier classifier) =>
        SubscribeCoreAsync(new EventHandlerBridge<TPayload>(handler, JsonOptions, classifier, _stateDefinitions));

    private async Task SubscribeCoreAsync(Native.EventHandler bridge)
    {
        var native = await NativeAsync(CancellationToken.None).ConfigureAwait(false);
        await native.Subscribe(bridge).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the consumer. You can subscribe again later.
    /// </summary>
    /// <remarks>
    /// A client that has not connected has no consumer to stop. This method returns at once in
    /// that case and never starts or waits on a build, so a worker's <c>StopAsync</c> cannot hang
    /// on a connect that has not finished.
    /// </remarks>
    public async Task UnsubscribeAsync()
    {
        Task<Native.ProsodyClient>? pending;
        lock (_gate)
        {
            pending = _native;
        }

        if (pending is { IsCompletedSuccessfully: true })
        {
            var native = await pending.ConfigureAwait(false);
            await native.Unsubscribe().ConfigureAwait(false);
        }
    }
}
