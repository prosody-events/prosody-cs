using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Context.Propagation;
using Prosody.Errors;
using Prosody.Logging;
using Prosody.Messaging;
using Prosody.State;
using NativeHandler = Prosody.Native.EventHandler;
using NativeResult = Prosody.Native.HandlerResult;

namespace Prosody.Infrastructure;

/// <summary>
/// Shared static infrastructure for bridge classes: activity source, logging, handler invocation, and Sentry helpers.
/// </summary>
internal static class EventHandlerBridge
{
    internal static readonly byte[] JsonNull = "null"u8.ToArray();
    private const string _loggerCategory = $"Prosody.{nameof(EventHandlerBridge)}";

    internal const string OnMessageActivityName = "on_message";
    internal const string OnExciseActivityName = "on_excise";
    internal const string OnTimerActivityName = "on_timer";

    internal static byte[] SerializeResponse<TResponse>(TResponse response, JsonTypeInfo<TResponse> typeInfo)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(response, typeInfo);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new PermanentException("The handler response is not valid JSON.", error);
        }
    }

    internal static TPayload? DeserializePayload<TPayload>(byte[] payload, JsonTypeInfo<TPayload> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(payload.AsSpan(), typeInfo);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new PermanentException("The message payload is not valid JSON.", error);
        }
    }

    // follow-up (AOT): Assembly.GetCustomAttribute is trim-unsafe; replace with a
    // source-generated constant (e.g. ThisAssembly.InformationalVersion via MinVer or
    // a generated AssemblyInfo property) so the version survives trimming/Native AOT.
    internal static readonly ActivitySource ActivitySource = new(
        "Prosody",
        typeof(EventHandlerBridge)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
    );

    // Resolved on each access so that test code can Clear/Configure ProsodyLogging
    // between tests. In production, Configure() is called once before any handler
    // fires, so the factory lookup is effectively constant after startup.
    private static ILogger Logger => ProsodyLogging.CreateLogger(_loggerCategory);

    internal static Dictionary<string, string> BuildMessageSentryContext(
        string topic,
        string key,
        int partition,
        long offset
    ) =>
        new(StringComparer.Ordinal)
        {
            ["topic"] = topic,
            ["key"] = key,
            ["partition"] = partition.ToString(CultureInfo.InvariantCulture),
            ["offset"] = offset.ToString(CultureInfo.InvariantCulture),
        };

    internal static Dictionary<string, string> BuildTimerSentryContext(ProsodyTimer timer) =>
        new(StringComparer.Ordinal)
        {
            ["key"] = timer.Key,
            ["time"] = timer.Time.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>
    /// Shared handler invocation logic: sets up CTS, bridges cancellation, invokes the handler,
    /// and classifies any exception as permanent or transient.
    /// Cancellation never detaches a handler. This method returns only after the handler returns.
    /// </summary>
    internal static async Task<NativeResult> InvokeHandlerAsync(
        Func<CancellationToken, Task<byte[]>> handler,
        Func<Exception, bool> isPermanentError,
        Func<CancellationToken, Task> onCancel,
        Dictionary<string, string> carrier,
        string activityName,
        string eventType = "handler",
        Func<Dictionary<string, string>?>? buildSentryContext = null
    )
    {
        PropagationContext propagation = TracePropagation.Extract(carrier);
        using var activity = ActivitySource.StartActivity(
            activityName,
            ActivityKind.Consumer,
            propagation.ActivityContext
        );
        var cts = new CancellationTokenSource();
        var monitorStop = new CancellationTokenSource();

        Task cancelMonitor = BridgeCancellationAsync(onCancel, cts, monitorStop.Token);

        try
        {
            byte[] response = await handler(cts.Token).ConfigureAwait(false);
            return new NativeResult.Success(response);
        }
        catch (Exception ex) when (isPermanentError(ex))
        {
            RecordExceptionOnActivity(activity, ex);
            TryCaptureToSentry(ex, eventType, buildSentryContext, ErrorClass.Permanent);
            return new NativeResult.PermanentError(ex.ToString());
        }
        catch (OperationCanceledException ex)
        {
            // Cancellation is normal during shutdown/rebalance — report to Rust but skip Sentry.
            return new NativeResult.TransientError(ex.ToString());
        }
#pragma warning disable CA1031 // FFI boundary: must catch all exceptions to classify and return appropriate result code to Rust
        catch (Exception ex)
        {
            RecordExceptionOnActivity(activity, ex);
            TryCaptureToSentry(ex, eventType, buildSentryContext, ErrorClass.Transient);
            return new NativeResult.TransientError(ex.ToString());
        }
#pragma warning restore CA1031
        finally
        {
            try
            {
                await monitorStop.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await cancelMonitor.ConfigureAwait(false);
                }
                finally
                {
                    monitorStop.Dispose();
                    cts.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Captures an exception to Sentry with event context, swallowing any failure so
    /// Sentry issues never mask or replace the original exception at the FFI boundary.
    /// </summary>
    private static void TryCaptureToSentry(
        Exception exception,
        string eventType,
        Func<Dictionary<string, string>?>? buildSentryContext,
        ErrorClass errorClass
    )
    {
        try
        {
            SentryIntegration.CaptureException(exception, eventType, buildSentryContext?.Invoke(), errorClass);
        }
#pragma warning disable CA1031 // FFI boundary: Sentry failures must not mask the original exception
        catch (Exception captureEx)
        {
            try
            {
                LogHelper.LogSentryCaptureFailed(Logger, captureEx);
            }
            catch
            {
                // Swallow — nothing must escape at the FFI boundary.
            }
        }
#pragma warning restore CA1031
    }

    internal static void RecordExceptionOnActivity(Activity? activity, Exception ex) =>
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message).AddException(ex);

    /// <summary>
    /// Bridges a cancellation signal to a <see cref="CancellationTokenSource"/>.
    /// </summary>
    /// <remarks>
    /// The stop token cancels the native wait after the handler returns.
    /// The method completes only after the native wait releases its resources.
    /// </remarks>
    internal static async Task BridgeCancellationAsync(
        Func<CancellationToken, Task> onCancel,
        CancellationTokenSource cts,
        CancellationToken stopToken
    )
    {
        Task cancelTask;
        try
        {
            cancelTask = onCancel(stopToken);
        }
#pragma warning disable CA1031 // Infrastructure — synchronous faults from OnCancel() must not propagate
        catch (Exception ex)
        {
            LogHelper.LogOnCancelSyncFault(Logger, ex);
            return;
        }
#pragma warning restore CA1031

        try
        {
            await cancelTask.ConfigureAwait(false);
            await cts.CancelAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Infrastructure faults must not cross the handler boundary
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && stopToken.IsCancellationRequested)
            {
                return;
            }
            LogHelper.LogOnCancelFault(Logger, ex);
        }
#pragma warning restore CA1031
    }
}

/// <summary>
/// Bridges a typed user-facing <see cref="IProsodyHandler{TPayload}"/> interface
/// to native event values.
/// </summary>
/// <remarks>
/// Deserializes the payload once per message, inside the protected handler scope so that
/// <see cref="JsonException"/> is classified by the error classification logic on
/// <see cref="IProsodyHandler{TPayload}.OnMessageAsync"/> exactly like any other exception.
/// </remarks>
internal sealed class EventHandlerBridge<TPayload>
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
    {
        ArgumentNullException.ThrowIfNull(userHandler);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        _onMessage = BindMessageHandler(userHandler);
        _onExcise = BindExciseHandler(userHandler);
        _onTimer = BindTimerHandler(userHandler);
        _jsonOptions = jsonOptions;
        _stateDefinitions = stateDefinitions ?? new HashSet<StateDefinition>(ReferenceEqualityComparer.Instance);
        _payloadTypeInfo = (JsonTypeInfo<TPayload>)jsonOptions.GetTypeInfo(typeof(TPayload));

        var handlerType = userHandler.GetType();
        var interfaceType = typeof(IProsodyHandler<TPayload>);
        var onMsgAttr = PermanentErrorResolver.GetAttribute(
            handlerType,
            interfaceType,
            nameof(IProsodyHandler<TPayload>.OnMessageAsync)
        );
        var onTimerAttr = PermanentErrorResolver.GetAttribute(
            handlerType,
            interfaceType,
            nameof(IProsodyHandler<TPayload>.OnTimerAsync)
        );
        var onExciseAttr = PermanentErrorResolver.GetAttribute(
            handlerType,
            interfaceType,
            nameof(IProsodyHandler<TPayload>.OnExciseAsync)
        );
        _isMessagePermanent = ex => PermanentErrorResolver.IsPermanentError(ex, onMsgAttr);
        _isExcisePermanent = ex => PermanentErrorResolver.IsPermanentError(ex, onExciseAttr);
        _isTimerPermanent = ex => PermanentErrorResolver.IsPermanentError(ex, onTimerAttr);
    }

    public EventHandlerBridge(
        IProsodyHandler<TPayload> userHandler,
        JsonSerializerOptions jsonOptions,
        IPermanentErrorClassifier classifier,
        IReadOnlySet<StateDefinition>? stateDefinitions = null
    )
    {
        ArgumentNullException.ThrowIfNull(userHandler);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        ArgumentNullException.ThrowIfNull(classifier);

        _onMessage = BindMessageHandler(userHandler);
        _onExcise = BindExciseHandler(userHandler);
        _onTimer = BindTimerHandler(userHandler);
        _jsonOptions = jsonOptions;
        _stateDefinitions = stateDefinitions ?? new HashSet<StateDefinition>(ReferenceEqualityComparer.Instance);
        _payloadTypeInfo = (JsonTypeInfo<TPayload>)jsonOptions.GetTypeInfo(typeof(TPayload));
        _isMessagePermanent = ex => ex is IPermanentError || classifier.IsMessageErrorPermanent(ex);
        _isExcisePermanent = ex => ex is IPermanentError || classifier.IsExciseErrorPermanent(ex);
        _isTimerPermanent = ex => ex is IPermanentError || classifier.IsTimerErrorPermanent(ex);
    }

    private EventHandlerBridge(
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition> stateDefinitions,
        Func<ProsodyContext, Message<TPayload>, CancellationToken, Task<byte[]>> onMessage,
        Func<ProsodyContext, ExciseMessage, CancellationToken, Task<byte[]>> onExcise,
        Func<ProsodyContext, ProsodyTimer, CancellationToken, Task<byte[]>> onTimer,
        Func<Exception, bool> isMessagePermanent,
        Func<Exception, bool> isExcisePermanent,
        Func<Exception, bool> isTimerPermanent
    )
    {
        _jsonOptions = jsonOptions;
        _stateDefinitions = stateDefinitions;
        _payloadTypeInfo = (JsonTypeInfo<TPayload>)jsonOptions.GetTypeInfo(typeof(TPayload));
        _onMessage = onMessage;
        _onExcise = onExcise;
        _onTimer = onTimer;
        _isMessagePermanent = isMessagePermanent;
        _isExcisePermanent = isExcisePermanent;
        _isTimerPermanent = isTimerPermanent;
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
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        var handlerType = handler.GetType();
        var interfaceType = typeof(IProsodyRequestHandler<TPayload, TResponse>);
        var messageAttribute = PermanentErrorResolver.GetAttribute(
            handlerType,
            interfaceType,
            nameof(IProsodyRequestHandler<TPayload, TResponse>.OnMessageAsync)
        );
        var timerAttribute = PermanentErrorResolver.GetAttribute(
            handlerType,
            interfaceType,
            nameof(IProsodyRequestHandler<TPayload, TResponse>.OnTimerAsync)
        );
        var exciseAttribute = PermanentErrorResolver.GetAttribute(
            handlerType,
            interfaceType,
            nameof(IProsodyRequestHandler<TPayload, TResponse>.OnExciseAsync)
        );
        return Responding(
            handler,
            jsonOptions,
            stateDefinitions,
            error => PermanentErrorResolver.IsPermanentError(error, messageAttribute),
            error => PermanentErrorResolver.IsPermanentError(error, exciseAttribute),
            error => PermanentErrorResolver.IsPermanentError(error, timerAttribute)
        );
    }

    internal static EventHandlerBridge<TPayload> Responding<TResponse>(
        IProsodyRequestHandler<TPayload, TResponse> handler,
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition> stateDefinitions,
        IPermanentErrorClassifier classifier
    )
    {
        ArgumentNullException.ThrowIfNull(classifier);
        return Responding(
            handler,
            jsonOptions,
            stateDefinitions,
            error => error is IPermanentError || classifier.IsMessageErrorPermanent(error),
            error => error is IPermanentError || classifier.IsExciseErrorPermanent(error),
            error => error is IPermanentError || classifier.IsTimerErrorPermanent(error)
        );
    }

    private static EventHandlerBridge<TPayload> Responding<TResponse>(
        IProsodyRequestHandler<TPayload, TResponse> handler,
        JsonSerializerOptions jsonOptions,
        IReadOnlySet<StateDefinition> stateDefinitions,
        Func<Exception, bool> isMessagePermanent,
        Func<Exception, bool> isExcisePermanent,
        Func<Exception, bool> isTimerPermanent
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        var responseTypeInfo = (JsonTypeInfo<TResponse>)jsonOptions.GetTypeInfo(typeof(TResponse));
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
            isMessagePermanent,
            isExcisePermanent,
            isTimerPermanent
        );
    }

    internal Task<NativeResult> OnMessage(
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

        return WithContext(
            new ProsodyContext(context, _jsonOptions, _stateDefinitions),
            prosodyContext =>
                HandleMessageAsync(
                    prosodyContext,
                    topic,
                    key,
                    partition,
                    offset,
                    timestamp,
                    bytes,
                    ct => context.OnCancel(ct),
                    carrier,
                    message
                )
        );
    }

    internal Task<NativeResult> OnExcise(
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
        return WithContext(
            new ProsodyContext(context, _jsonOptions, _stateDefinitions),
            prosodyContext =>
                EventHandlerBridge.InvokeHandlerAsync(
                    ct => _onExcise(prosodyContext, record, ct),
                    _isExcisePermanent,
                    ct => context.OnCancel(ct),
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
                )
        );
    }

    internal Task<NativeResult> OnTimer(
        Native.Context context,
        Native.Timer timer,
        Dictionary<string, string> carrier
    ) =>
        WithContext(
            new ProsodyContext(context, _jsonOptions, _stateDefinitions),
            prosodyContext =>
                HandleTimerAsync(prosodyContext, new ProsodyTimer(timer), ct => context.OnCancel(ct), carrier)
        );

    private static async Task<NativeResult> WithContext(
        ProsodyContext context,
        Func<ProsodyContext, Task<NativeResult>> handler
    )
    {
        try
        {
            return await handler(context).ConfigureAwait(false);
        }
        finally
        {
            context.Invalidate();
        }
    }

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
        Func<CancellationToken, Task> onCancel,
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
        Func<CancellationToken, Task> onCancel,
        Dictionary<string, string> carrier
    ) =>
        EventHandlerBridge.InvokeHandlerAsync(
            async ct =>
            {
                return await _onTimer(prosodyContext, wrappedTimer, ct).ConfigureAwait(false);
            },
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
