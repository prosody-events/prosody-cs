using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Prosody.Errors;
using Prosody.Messaging;
using Prosody.State;
using NativeHandler = Prosody.Native.EventHandler;
using NativeResult = Prosody.Native.HandlerResult;

namespace Prosody.Infrastructure;

/// <summary>
/// Bridges a typed user-facing <see cref="IProsodyHandler{TPayload}"/> interface
/// to the UniFFI-generated <see cref="NativeHandler"/> interface.
/// </summary>
/// <remarks>
/// <para>
/// Deserializes the payload once per message, inside the protected handler scope so that
/// <see cref="JsonException"/> is classified by the error classification logic on
/// <see cref="IProsodyHandler{TPayload}.OnMessageAsync"/> exactly like any other exception.
/// </para>
/// <para>
/// An exception is permanent when it implements <see cref="IPermanentError"/> or when the
/// bridge's <see cref="IPermanentErrorClassifier"/> classifies it as permanent. Every other
/// exception is transient.
/// </para>
/// </remarks>
internal sealed class EventHandlerBridge<TPayload> : NativeHandler
{
    private readonly Func<ProsodyContext, Message<TPayload>, CancellationToken, Task<byte[]>> _onMessage;
    private readonly Func<ProsodyContext, ExciseMessage, CancellationToken, Task<byte[]>> _onExcise;
    private readonly Func<ProsodyContext, ProsodyTimer, CancellationToken, Task<byte[]>> _onTimer;
    private readonly Func<Exception, bool> _isMessagePermanent;
    private readonly Func<Exception, bool> _isExcisePermanent;
    private readonly Func<Exception, bool> _isTimerPermanent;
    private readonly JsonTypeInfo<TPayload> _payloadTypeInfo;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IReadOnlySet<StateDefinition> _stateDefinitions;

    [RequiresUnreferencedCode(
        "Reads PermanentErrorAttribute from handler methods via reflection. Use the constructor that accepts IPermanentErrorClassifier to avoid the reflection path."
    )]
    [RequiresDynamicCode(
        "GetInterfaceMap requires the handler type's methods to be preserved. Use the constructor that accepts IPermanentErrorClassifier to avoid this requirement."
    )]
    public EventHandlerBridge(
        IProsodyHandler<TPayload> userHandler,
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition>? stateDefinitions = null
    )
        : this(
            userHandler,
            jsonOptions,
            PermanentErrorResolver.Classifier(userHandler, typeof(IProsodyHandler<TPayload>)),
            stateDefinitions
        ) { }

    public EventHandlerBridge(
        IProsodyHandler<TPayload> userHandler,
        JsonSerializerOptions jsonOptions,
        IPermanentErrorClassifier classifier,
        IReadOnlySet<StateDefinition>? stateDefinitions = null
    )
        : this(
            jsonOptions,
            stateDefinitions,
            BindMessageHandler(userHandler),
            BindExciseHandler(userHandler),
            BindTimerHandler(userHandler),
            classifier
        ) => ArgumentNullException.ThrowIfNull(userHandler);

    private EventHandlerBridge(
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition>? stateDefinitions,
        Func<ProsodyContext, Message<TPayload>, CancellationToken, Task<byte[]>> onMessage,
        Func<ProsodyContext, ExciseMessage, CancellationToken, Task<byte[]>> onExcise,
        Func<ProsodyContext, ProsodyTimer, CancellationToken, Task<byte[]>> onTimer,
        IPermanentErrorClassifier classifier
    )
    {
        ArgumentNullException.ThrowIfNull(jsonOptions);
        ArgumentNullException.ThrowIfNull(classifier);

        _jsonOptions = jsonOptions;
        _stateDefinitions = stateDefinitions ?? new HashSet<StateDefinition>(ReferenceEqualityComparer.Instance);
        _payloadTypeInfo = StateInterop.ResolveTypeInfo<TPayload>(jsonOptions);
        _onMessage = onMessage;
        _onExcise = onExcise;
        _onTimer = onTimer;
        _isMessagePermanent = error => error is IPermanentError || classifier.IsMessageErrorPermanent(error);
        _isExcisePermanent = error => error is IPermanentError || classifier.IsExciseErrorPermanent(error);
        _isTimerPermanent = error => error is IPermanentError || classifier.IsTimerErrorPermanent(error);
    }

    private static Func<ProsodyContext, Message<TPayload>, CancellationToken, Task<byte[]>> BindMessageHandler(
        IProsodyHandler<TPayload> handler
    ) =>
        async (context, message, cancellationToken) =>
        {
            await handler.OnMessageAsync(context, message, cancellationToken).ConfigureAwait(false);
            return EventHandlerBridge.JsonNull;
        };

    private static Func<ProsodyContext, ExciseMessage, CancellationToken, Task<byte[]>> BindExciseHandler(
        IProsodyHandler<TPayload> handler
    ) =>
        async (context, message, cancellationToken) =>
        {
            await handler.OnExciseAsync(context, message, cancellationToken).ConfigureAwait(false);
            return EventHandlerBridge.JsonNull;
        };

    private static Func<ProsodyContext, ProsodyTimer, CancellationToken, Task<byte[]>> BindTimerHandler(
        IProsodyHandler<TPayload> handler
    ) =>
        async (context, timer, cancellationToken) =>
        {
            await handler.OnTimerAsync(context, timer, cancellationToken).ConfigureAwait(false);
            return EventHandlerBridge.JsonNull;
        };

    [RequiresUnreferencedCode("Reads PermanentErrorAttribute from handler methods via reflection.")]
    [RequiresDynamicCode("GetInterfaceMap requires handler methods at run time.")]
    internal static EventHandlerBridge<TPayload> Responding<TResponse>(
        IProsodyRequestHandler<TPayload, TResponse> handler,
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition> stateDefinitions
    ) =>
        Responding(
            handler,
            jsonOptions,
            stateDefinitions,
            PermanentErrorResolver.Classifier(handler, typeof(IProsodyRequestHandler<TPayload, TResponse>))
        );

    internal static EventHandlerBridge<TPayload> Responding<TResponse>(
        IProsodyRequestHandler<TPayload, TResponse> handler,
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition> stateDefinitions,
        IPermanentErrorClassifier classifier
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var responseTypeInfo = StateInterop.ResolveTypeInfo<TResponse>(jsonOptions);
        return new EventHandlerBridge<TPayload>(
            jsonOptions,
            stateDefinitions,
            async (context, message, cancellationToken) =>
                EventHandlerBridge.SerializeResponse(
                    await handler.OnMessageAsync(context, message, cancellationToken).ConfigureAwait(false),
                    responseTypeInfo
                ),
            async (context, message, cancellationToken) =>
                EventHandlerBridge.SerializeResponse(
                    await handler.OnExciseAsync(context, message, cancellationToken).ConfigureAwait(false),
                    responseTypeInfo
                ),
            async (context, timer, cancellationToken) =>
            {
                await handler.OnTimerAsync(context, timer, cancellationToken).ConfigureAwait(false);
                return EventHandlerBridge.JsonNull;
            },
            classifier
        );
    }

    /// <inheritdoc/>
    public Task<NativeResult> OnMessage(
        Native.Context context,
        Native.Message message,
        Dictionary<string, string> carrier
    )
    {
        // Eagerly capture all native fields before any async suspension — each accessor
        // crosses the FFI boundary and the native message object cannot be accessed after
        // the handler scope returns to Rust.
        var topic = message.Topic();
        var key = message.Key();
        var partition = message.Partition();
        var offset = message.Offset();
        var timestamp = new DateTimeOffset(message.Timestamp(), TimeSpan.Zero);
        var bytes = message.Payload();

        return HandleMessageAsync(
            new ProsodyContext(context, _jsonOptions, _stateDefinitions),
            topic,
            key,
            partition,
            offset,
            timestamp,
            bytes,
            context.OnCancel,
            carrier,
            message
        );
    }

    /// <inheritdoc/>
    public Task<NativeResult> OnExcise(
        Native.Context context,
        Native.ExciseMessage message,
        Dictionary<string, string> carrier
    )
    {
        var record = new ExciseMessage(
            message.Topic(),
            message.Key(),
            message.Partition(),
            message.Offset(),
            new DateTimeOffset(message.Timestamp(), TimeSpan.Zero)
        );
        return EventHandlerBridge.InvokeHandlerAsync(
            ct => _onExcise(new ProsodyContext(context, _jsonOptions, _stateDefinitions), record, ct),
            _isExcisePermanent,
            context.OnCancel,
            carrier,
            activityName: EventHandlerBridge.OnExciseActivityName,
            eventType: SentryConstants.TagValues.EventTypeExcise,
            buildSentryContext: SentryIntegration.IsEnabled
                ? () =>
                    EventHandlerBridge.BuildMessageSentryContext(
                        record.Topic,
                        record.Key,
                        record.Partition,
                        record.Offset
                    )
                : null
        );
    }

    /// <inheritdoc/>
    public Task<NativeResult> OnTimer(Native.Context context, Native.Timer timer, Dictionary<string, string> carrier) =>
        HandleTimerAsync(
            new ProsodyContext(context, _jsonOptions, _stateDefinitions),
            new ProsodyTimer(timer),
            context.OnCancel,
            carrier
        );

    /// <summary>
    /// Core message handling logic, decoupled from native types for testability.
    /// Deserialization runs inside the handler closure so <see cref="JsonException"/> is
    /// classified by the bridge's error classification logic exactly like any other exception.
    /// </summary>
    internal Task<NativeResult> HandleMessageAsync(
        ProsodyContext prosodyContext,
        string topic,
        string key,
        int partition,
        long offset,
        DateTimeOffset timestamp,
        byte[] payload,
        Func<Task> onCancel,
        Dictionary<string, string> carrier,
        Native.Message? nativeMessage = null
    ) =>
        EventHandlerBridge.InvokeHandlerAsync(
            async ct =>
            {
                var deserialized = EventHandlerBridge.DeserializePayload(payload, _payloadTypeInfo);
                var msg = new Message<TPayload>(topic, key, partition, offset, timestamp, deserialized, nativeMessage);
                return await _onMessage(prosodyContext, msg, ct).ConfigureAwait(false);
            },
            _isMessagePermanent,
            onCancel,
            carrier,
            activityName: EventHandlerBridge.OnMessageActivityName,
            eventType: SentryConstants.TagValues.EventTypeMessage,
            buildSentryContext: SentryIntegration.IsEnabled
                ? () => EventHandlerBridge.BuildMessageSentryContext(topic, key, partition, offset)
                : null
        );

    /// <summary>
    /// Core timer handling logic, decoupled from native types for testability.
    /// </summary>
    internal Task<NativeResult> HandleTimerAsync(
        ProsodyContext prosodyContext,
        ProsodyTimer wrappedTimer,
        Func<Task> onCancel,
        Dictionary<string, string> carrier
    ) =>
        EventHandlerBridge.InvokeHandlerAsync(
            ct => _onTimer(prosodyContext, wrappedTimer, ct),
            _isTimerPermanent,
            onCancel,
            carrier,
            activityName: EventHandlerBridge.OnTimerActivityName,
            eventType: SentryConstants.TagValues.EventTypeTimer,
            buildSentryContext: SentryIntegration.IsEnabled
                ? () => EventHandlerBridge.BuildTimerSentryContext(wrappedTimer)
                : null
        );
}
