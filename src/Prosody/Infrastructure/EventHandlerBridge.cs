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
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Infrastructure;

/// <summary>
/// Shared static infrastructure for bridge classes: activity source, logging, handler invocation, and Sentry helpers.
/// </summary>
internal static class EventHandlerBridge
{
    private const string _loggerCategory = $"Prosody.{nameof(EventHandlerBridge)}";

    internal const string OnMessageActivityName = "on_message";
    internal const string OnTimerActivityName = "on_timer";

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
    /// Invokes a handler with a registered cancellation source and classifies
    /// its result as permanent or transient.
    /// </summary>
    internal static async Task<NativeResult> InvokeHandlerAsync(
        Func<CancellationToken, Task> handler,
        Func<Exception, bool> isPermanentError,
        CancellationRegistry cancellations,
        ulong handlerId,
        Dictionary<string, string> carrier,
        string activityName,
        string eventType = "handler",
        Func<Dictionary<string, string>?>? buildSentryContext = null,
        Func<bool>? shouldCancel = null
    )
    {
        PropagationContext propagation = TracePropagation.Extract(carrier);
        using var activity = ActivitySource.StartActivity(
            activityName,
            ActivityKind.Consumer,
            propagation.ActivityContext
        );
        CancellationTokenSource source = cancellations.Register(handlerId);

        // Rust can signal cancellation before this invocation registers its
        // source: the generated shim starts handlers with Task.Run, so
        // Cancel(handlerId) can run first and find nothing. Probe the
        // pull-based signal once after the registration to close that window.
        // The cancel completes inline because the source has no registrations.
        if (shouldCancel?.Invoke() == true)
        {
            await source.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await handler(source.Token).ConfigureAwait(false);
            return new NativeResult(NativeResultCode.Success, ErrorMessage: null);
        }
        catch (Exception ex) when (isPermanentError(ex))
        {
            RecordExceptionOnActivity(activity, ex);
            TryCaptureToSentry(ex, eventType, buildSentryContext, ErrorClass.Permanent);
            return new NativeResult(NativeResultCode.PermanentError, ex.ToString());
        }
        catch (OperationCanceledException ex)
        {
            // Cancellation is normal during shutdown/rebalance — report to Rust but skip Sentry.
            return new NativeResult(NativeResultCode.TransientError, ex.ToString());
        }
#pragma warning disable CA1031 // FFI boundary: must catch all exceptions to classify and return appropriate result code to Rust
        catch (Exception ex)
        {
            RecordExceptionOnActivity(activity, ex);
            TryCaptureToSentry(ex, eventType, buildSentryContext, ErrorClass.Transient);
            return new NativeResult(NativeResultCode.TransientError, ex.ToString());
        }
#pragma warning restore CA1031
        finally
        {
            cancellations.Complete(handlerId);
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
}

/// <summary>
/// Bridges a typed user-facing <see cref="IProsodyHandler{TPayload}"/> interface
/// to the UniFFI-generated <see cref="NativeHandler"/> interface.
/// </summary>
/// <remarks>
/// Deserializes the payload once per message, inside the protected handler scope so that
/// <see cref="JsonException"/> is classified by the error classification logic on
/// <see cref="IProsodyHandler{TPayload}.OnMessageAsync"/> exactly like any other exception.
/// </remarks>
internal sealed class EventHandlerBridge<TPayload> : NativeHandler
{
    private readonly IProsodyHandler<TPayload> _userHandler;
    private readonly Func<Exception, bool> _isMessagePermanent;
    private readonly Func<Exception, bool> _isTimerPermanent;
    private readonly JsonTypeInfo<TPayload> _payloadTypeInfo;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IReadOnlySet<StateDefinition> _stateDefinitions;
    private readonly CancellationRegistry _cancellations = new();

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

        _userHandler = userHandler;
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
        _isMessagePermanent = ex => PermanentErrorResolver.IsPermanentError(ex, onMsgAttr);
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

        _userHandler = userHandler;
        _jsonOptions = jsonOptions;
        _stateDefinitions = stateDefinitions ?? new HashSet<StateDefinition>(ReferenceEqualityComparer.Instance);
        _payloadTypeInfo = (JsonTypeInfo<TPayload>)jsonOptions.GetTypeInfo(typeof(TPayload));
        _isMessagePermanent = ex => ex is IPermanentError || classifier.IsMessageErrorPermanent(ex);
        _isTimerPermanent = ex => ex is IPermanentError || classifier.IsTimerErrorPermanent(ex);
    }

    /// <inheritdoc/>
    public Task<NativeResult> OnMessage(
        Native.Context context,
        Native.Message message,
        Dictionary<string, string> carrier,
        ulong handlerId
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
            carrier,
            handlerId,
            message,
            context.ShouldCancel
        );
    }

    /// <inheritdoc/>
    public Task<NativeResult> OnTimer(
        Native.Context context,
        Native.Timer timer,
        Dictionary<string, string> carrier,
        ulong handlerId
    ) =>
        HandleTimerAsync(
            new ProsodyContext(context, _jsonOptions, _stateDefinitions),
            new ProsodyTimer(timer),
            carrier,
            handlerId,
            context.ShouldCancel
        );

    /// <inheritdoc/>
    public void Cancel(ulong handlerId) => _cancellations.Cancel(handlerId);

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
        Dictionary<string, string> carrier,
        ulong handlerId,
        Native.Message? nativeMessage = null,
        Func<bool>? shouldCancel = null
    ) =>
        EventHandlerBridge.InvokeHandlerAsync(
            ct =>
            {
                var deserialized = JsonSerializer.Deserialize(payload.AsSpan(), _payloadTypeInfo);
                var msg = new Message<TPayload>(topic, key, partition, offset, timestamp, deserialized, nativeMessage);
                return _userHandler.OnMessageAsync(prosodyContext, msg, ct);
            },
            _isMessagePermanent,
            _cancellations,
            handlerId,
            carrier,
            activityName: EventHandlerBridge.OnMessageActivityName,
            eventType: SentryConstants.TagValues.EventTypeMessage,
            buildSentryContext: SentryIntegration.IsEnabled
                ? () => EventHandlerBridge.BuildMessageSentryContext(topic, key, partition, offset)
                : null,
            shouldCancel: shouldCancel
        );

    /// <summary>
    /// Core timer handling logic, decoupled from native types for testability.
    /// </summary>
    internal Task<NativeResult> HandleTimerAsync(
        ProsodyContext prosodyContext,
        ProsodyTimer wrappedTimer,
        Dictionary<string, string> carrier,
        ulong handlerId,
        Func<bool>? shouldCancel = null
    ) =>
        EventHandlerBridge.InvokeHandlerAsync(
            ct => _userHandler.OnTimerAsync(prosodyContext, wrappedTimer, ct),
            _isTimerPermanent,
            _cancellations,
            handlerId,
            carrier,
            activityName: EventHandlerBridge.OnTimerActivityName,
            eventType: SentryConstants.TagValues.EventTypeTimer,
            buildSentryContext: SentryIntegration.IsEnabled
                ? () => EventHandlerBridge.BuildTimerSentryContext(wrappedTimer)
                : null,
            shouldCancel: shouldCancel
        );
}
