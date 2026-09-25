using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Context.Propagation;
using Prosody.Errors;
using Prosody.Logging;
using Prosody.Messaging;
using NativeResult = Prosody.Native.HandlerResult;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody.Infrastructure;

/// <summary>
/// Shared static infrastructure for bridge classes: activity source, logging, handler invocation, and Sentry helpers.
/// </summary>
/// <remarks>The typed bridge itself is <see cref="EventHandlerBridge{TPayload}"/>.</remarks>
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
    /// </summary>
    internal static async Task<NativeResult> InvokeHandlerAsync(
        Func<CancellationToken, Task<byte[]>> handler,
        Func<Exception, bool> isPermanentError,
        Func<Task> onCancel,
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
            byte[] response = await handler(cts.Token).ConfigureAwait(false);
            return new NativeResult(NativeResultCode.Success, ErrorMessage: null, response);
        }
        catch (Exception ex) when (isPermanentError(ex))
        {
            RecordExceptionOnActivity(activity, ex);
            TryCaptureToSentry(ex, eventType, buildSentryContext, ErrorClass.Permanent);
            return new NativeResult(NativeResultCode.PermanentError, ex.ToString(), JsonNull);
        }
        catch (OperationCanceledException ex)
        {
            // Cancellation is normal during shutdown/rebalance — report to Rust but skip Sentry.
            return new NativeResult(NativeResultCode.TransientError, ex.ToString(), JsonNull);
        }
#pragma warning disable CA1031 // FFI boundary: must catch all exceptions to classify and return appropriate result code to Rust
        catch (Exception ex)
        {
            RecordExceptionOnActivity(activity, ex);
            TryCaptureToSentry(ex, eventType, buildSentryContext, ErrorClass.Transient);
            return new NativeResult(NativeResultCode.TransientError, ex.ToString(), JsonNull);
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
