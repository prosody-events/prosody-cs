using System.Globalization;
using Microsoft.Extensions.Logging;
using Prosody.Errors;
using Prosody.Logging;
using Prosody.Messaging;
using NativeHandler = Prosody.Native.EventHandler;
using NativeResult = Prosody.Native.HandlerResult;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Infrastructure;

/// <summary>
/// Bridges the user-facing <see cref="IProsodyHandler"/> interface
/// to the UniFFI-generated <see cref="NativeHandler"/> interface.
/// </summary>
/// <remarks>
/// This wrapper:
/// <list type="bullet">
///   <item>Wraps native types in their public wrapper equivalents</item>
///   <item>Creates a <see cref="CancellationToken"/> linked to the context's cancellation signal</item>
///   <item>Classifies exceptions as permanent or transient based on:
///     <list type="number">
///       <item><see cref="IPermanentError"/> marker interface (highest priority)</item>
///       <item><see cref="PermanentErrorAttribute"/> on the handler method</item>
///       <item>Default: transient (will retry)</item>
///     </list>
///   </item>
/// </list>
/// </remarks>
internal sealed class EventHandlerBridge : NativeHandler
{
    // Lazy so CreateLogger runs on first log, not at class load — ensures
    // ProsodyLogging.Configure() has been called. Unlike LogSinkBridge (which
    // receives ILogger via its constructor during Configure), this class is a
    // static consumer that cannot participate in the configuration lifecycle.
    private static readonly Lazy<ILogger> LazyLogger = new(
        () => ProsodyLogging.CreateLogger($"Prosody.{nameof(EventHandlerBridge)}")
    );

    private static ILogger Logger => LazyLogger.Value;

    private readonly IProsodyHandler _userHandler;
    private readonly PermanentErrorAttribute? _onMessageAttribute;
    private readonly PermanentErrorAttribute? _onTimerAttribute;

    /// <summary>
    /// Creates a new wrapper around the user's event handler.
    /// </summary>
    /// <param name="userHandler">The user's event handler implementation.</param>
    public EventHandlerBridge(IProsodyHandler userHandler)
    {
        ArgumentNullException.ThrowIfNull(userHandler);
        _userHandler = userHandler;

        // Resolve attributes once at construction time (cached across instances of the same handler type)
        var handlerType = userHandler.GetType();
        _onMessageAttribute = PermanentErrorResolver.GetAttribute(handlerType, nameof(IProsodyHandler.OnMessageAsync));
        _onTimerAttribute = PermanentErrorResolver.GetAttribute(handlerType, nameof(IProsodyHandler.OnTimerAsync));
    }

    /// <inheritdoc/>
    public Task<NativeResult> OnMessage(
        Native.Context context,
        Native.Message message,
        Dictionary<string, string> carrier
    ) => HandleMessageAsync(new ProsodyContext(context), new Message(message), context.OnCancel, carrier);

    /// <inheritdoc/>
    public Task<NativeResult> OnTimer(Native.Context context, Native.Timer timer, Dictionary<string, string> carrier) =>
        HandleTimerAsync(new ProsodyContext(context), new ProsodyTimer(timer), context.OnCancel, carrier);

    /// <summary>
    /// Core message handling logic, decoupled from native types for testability.
    /// </summary>
    internal Task<NativeResult> HandleMessageAsync(
        ProsodyContext wrappedContext,
        Message wrappedMessage,
        Func<Task> onCancel,
        Dictionary<string, string> carrier
    ) =>
        InvokeHandlerAsync(
            ct => _userHandler.OnMessageAsync(wrappedContext, wrappedMessage, ct),
            _onMessageAttribute,
            onCancel,
            carrier,
            wrappedMessage is null
                ? null
                : new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["event_type"] = "message",
                    ["topic"] = wrappedMessage.Topic,
                    ["key"] = wrappedMessage.Key,
                    ["partition"] = wrappedMessage.Partition.ToString(CultureInfo.InvariantCulture),
                    ["offset"] = wrappedMessage.Offset.ToString(CultureInfo.InvariantCulture),
                }
        );

    /// <summary>
    /// Core timer handling logic, decoupled from native types for testability.
    /// </summary>
    internal Task<NativeResult> HandleTimerAsync(
        ProsodyContext wrappedContext,
        ProsodyTimer wrappedTimer,
        Func<Task> onCancel,
        Dictionary<string, string> carrier
    ) =>
        InvokeHandlerAsync(
            ct => _userHandler.OnTimerAsync(wrappedContext, wrappedTimer, ct),
            _onTimerAttribute,
            onCancel,
            carrier,
            wrappedTimer is null
                ? null
                : new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["event_type"] = "timer",
                    ["key"] = wrappedTimer.Key,
                    ["time"] = wrappedTimer.Time.ToString(CultureInfo.InvariantCulture),
                }
        );

    /// <summary>
    /// Shared handler invocation logic: sets up CTS, bridges cancellation, invokes the handler,
    /// and classifies any exception as permanent or transient.
    /// </summary>
    private static async Task<NativeResult> InvokeHandlerAsync(
        Func<CancellationToken, Task> handler,
        PermanentErrorAttribute? permanentErrorAttribute,
        Func<Task> onCancel,
        Dictionary<string, string> carrier,
        Dictionary<string, string>? sentryContext = null
    )
    {
        using var activity = TracePropagation.Extract(carrier);
        using var cts = new CancellationTokenSource();
        var handlerDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Start the cancellation bridge — races OnCancel() against handler completion
        // so the monitor exits promptly regardless of which finishes first.
        // Awaited in finally to ensure the monitor itself completes before CTS disposal.
#pragma warning disable CA2025 // CTS outlives the monitor: finally awaits the monitor before the using scope disposes the CTS
        Task cancelMonitor = BridgeCancellationAsync(onCancel, cts, handlerDone.Task);
#pragma warning restore CA2025

        try
        {
            await handler(cts.Token).ConfigureAwait(false);
            return new NativeResult(NativeResultCode.Success, ErrorMessage: null);
        }
        catch (Exception ex) when (PermanentErrorResolver.IsPermanentError(ex, permanentErrorAttribute))
        {
            SentryIntegration.CaptureException(ex, sentryContext?["event_type"] ?? "handler", sentryContext);
            return new NativeResult(NativeResultCode.PermanentError, ex.ToString());
        }
#pragma warning disable CA1031 // FFI boundary: must catch all exceptions to classify and return appropriate result code to Rust
        catch (Exception ex)
        {
            SentryIntegration.CaptureException(ex, sentryContext?["event_type"] ?? "handler", sentryContext);
            return new NativeResult(NativeResultCode.TransientError, ex.ToString());
        }
#pragma warning restore CA1031
        finally
        {
            // Signal the monitor to stop waiting, then await it so no task leaks.
            // The using-scoped CTS is disposed after this finally block completes,
            // guaranteeing it outlives any CancelAsync() call inside the monitor.
            handlerDone.TrySetResult();
            await cancelMonitor.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Bridges a cancellation signal to a <see cref="CancellationTokenSource"/>.
    /// </summary>
    /// <remarks>
    /// Races <paramref name="onCancel"/> against <paramref name="handlerDone"/> so the
    /// monitor exits promptly whether cancellation arrives or the handler completes first.
    /// When the handler completes first, the <c>OnCancel()</c> task (which may block
    /// indefinitely in native code) is observed via a fault-swallowing continuation to
    /// prevent <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// Callers must <c>await</c> the returned task in a <see langword="finally"/> block after signalling
    /// <paramref name="handlerDone"/>.
    /// </remarks>
    internal static async Task BridgeCancellationAsync(
        Func<Task> onCancel,
        CancellationTokenSource cts,
        Task handlerDone
    )
    {
        Task cancelTask;
        try
        {
            cancelTask = onCancel();
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
            var completed = await Task.WhenAny(cancelTask, handlerDone).ConfigureAwait(false);

            if (completed != handlerDone)
            {
                // OnCancel() won the race — observe it (may have faulted) then trigger the CTS so the handler sees cancellation.
                await cancelTask.ConfigureAwait(false);
                try
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // CTS already disposed — handler completed between WhenAny and here.
                }
            }
            else
            {
                // Handler completed first.
                // The cancelTask may still be running (native OnCancel() can block indefinitely) or may fault later.
                // Attach a continuation to observe any future fault and prevent UnobservedTaskException.
                _ = cancelTask.ContinueWith(
                    static t => LogHelper.LogOnCancelLateFault(Logger, t.Exception),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            }
        }
#pragma warning disable CA1031, RCS1075 // Infrastructure — faults from OnCancel() must not propagate to the handler
        catch (Exception ex)
        {
            // OnCancel() faulted (e.g., native context was torn down). Nothing useful to do —
            // the handler will complete on its own or observe cancellation via ShouldCancel.
            LogHelper.LogOnCancelFault(Logger, ex);
        }
#pragma warning restore CA1031, RCS1075
    }
}
