using System.Diagnostics;
using System.Reflection;
using NativeHandler = Prosody.Native.EventHandler;
using NativeResult = Prosody.Native.HandlerResult;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody;

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

        // Read attributes once at construction time
        var handlerType = userHandler.GetType();
        _onMessageAttribute = GetPermanentErrorAttribute(handlerType, nameof(IProsodyHandler.OnMessageAsync));
        _onTimerAttribute = GetPermanentErrorAttribute(handlerType, nameof(IProsodyHandler.OnTimerAsync));
    }

    /// <inheritdoc/>
    public Task<NativeResult> OnMessage(
        Native.Context context,
        Native.Message message,
        Dictionary<string, string> carrier
    ) => HandleMessageAsync(new ProsodyContext(context), new Message(message), context.OnCancel, carrier);

    /// <inheritdoc/>
    public Task<NativeResult> OnTimer(Native.Context context, Native.Timer timer, Dictionary<string, string> carrier) =>
        HandleTimerAsync(new ProsodyContext(context), new Timer(timer), context.OnCancel, carrier);

    /// <summary>
    /// Core message handling logic, decoupled from native types for testability.
    /// </summary>
    internal async Task<NativeResult> HandleMessageAsync(
        ProsodyContext wrappedContext,
        Message wrappedMessage,
        Func<Task> onCancel,
        Dictionary<string, string> carrier
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
            await _userHandler.OnMessageAsync(wrappedContext, wrappedMessage, cts.Token).ConfigureAwait(false);
            return new NativeResult(NativeResultCode.Success, ErrorMessage: null);
        }
        catch (Exception ex) when (IsPermanentError(ex, _onMessageAttribute))
        {
            return new NativeResult(NativeResultCode.PermanentError, ex.ToString());
        }
#pragma warning disable CA1031 // FFI boundary: must catch all exceptions to classify and return appropriate result code to Rust
        catch (Exception ex)
        {
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
    /// Core timer handling logic, decoupled from native types for testability.
    /// </summary>
    internal async Task<NativeResult> HandleTimerAsync(
        ProsodyContext wrappedContext,
        Timer wrappedTimer,
        Func<Task> onCancel,
        Dictionary<string, string> carrier
    )
    {
        using var activity = TracePropagation.Extract(carrier);
        using var cts = new CancellationTokenSource();
        var handlerDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Bridge native cancellation to CTS (see HandleMessageAsync for detailed comments)
#pragma warning disable CA2025 // CTS outlives the monitor: finally awaits the monitor before the using scope disposes the CTS
        Task cancelMonitor = BridgeCancellationAsync(onCancel, cts, handlerDone.Task);
#pragma warning restore CA2025

        try
        {
            await _userHandler.OnTimerAsync(wrappedContext, wrappedTimer, cts.Token).ConfigureAwait(false);
            return new NativeResult(NativeResultCode.Success, null);
        }
        catch (Exception ex) when (IsPermanentError(ex, _onTimerAttribute))
        {
            return new NativeResult(NativeResultCode.PermanentError, ex.ToString());
        }
#pragma warning disable CA1031 // FFI boundary: must catch all exceptions to classify and return appropriate result code to Rust
        catch (Exception ex)
        {
            return new NativeResult(NativeResultCode.TransientError, ex.ToString());
        }
#pragma warning restore CA1031
        finally
        {
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
            Debug.WriteLine($"Prosody: OnCancel() faulted synchronously: {ex}");
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
                    static t => Debug.WriteLine($"Prosody: OnCancel() faulted after handler completed: {t.Exception}"),
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
            Debug.WriteLine($"Prosody: OnCancel() faulted: {ex}");
        }
#pragma warning restore CA1031, RCS1075
    }

    /// <summary>
    /// Determines whether an exception represents a permanent error.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <param name="attribute">The method's <see cref="PermanentErrorAttribute"/>, if any.</param>
    /// <returns>
    /// <see langword="true"/> if the exception is permanent (should not retry); otherwise, <see langword="false"/>.
    /// </returns>
    private static bool IsPermanentError(Exception exception, PermanentErrorAttribute? attribute)
    {
        // Priority 1: IPermanentError marker interface (runtime decision)
        if (exception is IPermanentError)
        {
            return true;
        }

        // Priority 2: PermanentErrorAttribute on the method (declaration-time)
        if (attribute?.IsMatch(exception) == true)
        {
            return true;
        }

        // Default: transient (will retry)
        return false;
    }

    /// <summary>
    /// Gets the <see cref="PermanentErrorAttribute"/> from a handler method, if present.
    /// </summary>
    /// <param name="handlerType">The handler implementation type.</param>
    /// <param name="methodName">The method name to inspect.</param>
    /// <returns>The attribute if found; otherwise, <see langword="null"/>.</returns>
    private static PermanentErrorAttribute? GetPermanentErrorAttribute(Type handlerType, string methodName)
    {
        // First, try the concrete type with both public and non-public bindings.
        // Non-public is needed for explicit interface implementations (which are private).
        // inherit: true walks the inheritance chain for base class attributes.
        var method = handlerType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );

        var attribute = method?.GetCustomAttribute<PermanentErrorAttribute>(inherit: true);
        if (attribute is not null)
        {
            return attribute;
        }

        // Fall back to the interface map — resolves the concrete method that implements
        // the interface method, which may carry the attribute even when the name doesn't
        // match (e.g., explicit implementations like IProsodyHandler.OnMessageAsync).
        var interfaceMethod = typeof(IProsodyHandler).GetMethod(methodName);
        if (interfaceMethod is null)
        {
            return null;
        }

        var mapping = handlerType.GetInterfaceMap(typeof(IProsodyHandler));
        for (var i = 0; i < mapping.InterfaceMethods.Length; i++)
        {
            if (mapping.InterfaceMethods[i] == interfaceMethod)
            {
                return mapping.TargetMethods[i].GetCustomAttribute<PermanentErrorAttribute>(inherit: true);
            }
        }

        return null;
    }
}
