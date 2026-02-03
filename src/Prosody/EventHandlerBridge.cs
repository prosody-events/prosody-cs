using System.Reflection;
using NativeHandler = Prosody.Native.EventHandler;
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
        _userHandler = userHandler ?? throw new ArgumentNullException(nameof(userHandler));

        // Read attributes once at construction time
        var handlerType = userHandler.GetType();
        _onMessageAttribute = GetPermanentErrorAttribute(
            handlerType,
            nameof(IProsodyHandler.OnMessageAsync)
        );
        _onTimerAttribute = GetPermanentErrorAttribute(
            handlerType,
            nameof(IProsodyHandler.OnTimerAsync)
        );
    }

    /// <inheritdoc/>
    public async Task<NativeResultCode> OnMessage(
        Native.Context context,
        Native.Message message,
        Dictionary<string, string> carrier
    )
    {
        using var activity = TracePropagation.Extract(carrier);
        using var cts = new CancellationTokenSource();

        _ = context
            .OnCancel()
            .ContinueWith(
                _ => cts.Cancel(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );

        var wrappedContext = new Context(context);
        var wrappedMessage = new Message(message);

        try
        {
            await _userHandler
                .OnMessageAsync(wrappedContext, wrappedMessage, cts.Token)
                .ConfigureAwait(false);
            return NativeResultCode.Success;
        }
        catch (OperationCanceledException) when (context.ShouldCancel())
        {
            return NativeResultCode.Cancelled;
        }
        catch (Exception ex) when (IsPermanentError(ex, _onMessageAttribute))
        {
            return NativeResultCode.PermanentError;
        }
#pragma warning disable CA1031 // FFI boundary: must catch all exceptions to classify and return appropriate result code to Rust
        catch
        {
            return NativeResultCode.TransientError;
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc/>
    public async Task<NativeResultCode> OnTimer(
        Native.Context context,
        Native.Timer timer,
        Dictionary<string, string> carrier
    )
    {
        using var activity = TracePropagation.Extract(carrier);
        using var cts = new CancellationTokenSource();

        _ = context
            .OnCancel()
            .ContinueWith(
                _ => cts.Cancel(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );

        var wrappedContext = new Context(context);
        var wrappedTimer = new Timer(timer);

        try
        {
            await _userHandler
                .OnTimerAsync(wrappedContext, wrappedTimer, cts.Token)
                .ConfigureAwait(false);
            return NativeResultCode.Success;
        }
        catch (OperationCanceledException) when (context.ShouldCancel())
        {
            return NativeResultCode.Cancelled;
        }
        catch (Exception ex) when (IsPermanentError(ex, _onTimerAttribute))
        {
            return NativeResultCode.PermanentError;
        }
#pragma warning disable CA1031 // FFI boundary: must catch all exceptions to classify and return appropriate result code to Rust
        catch
        {
            return NativeResultCode.TransientError;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Determines whether an exception represents a permanent error.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <param name="attribute">The method's <see cref="PermanentErrorAttribute"/>, if any.</param>
    /// <returns>
    /// <c>true</c> if the exception is permanent (should not retry); otherwise, <c>false</c>.
    /// </returns>
    private static bool IsPermanentError(Exception exception, PermanentErrorAttribute? attribute)
    {
        // Priority 1: IPermanentError marker interface (runtime decision)
        if (exception is IPermanentError)
        {
            return true;
        }

        // Priority 2: PermanentErrorAttribute on the method (declaration-time)
        if (attribute is not null && attribute.IsMatch(exception))
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
    /// <returns>The attribute if found; otherwise, <c>null</c>.</returns>
    private static PermanentErrorAttribute? GetPermanentErrorAttribute(
        Type handlerType,
        string methodName
    )
    {
        // Look for the method on the concrete type first, then interface
        var method = handlerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);

        return method?.GetCustomAttribute<PermanentErrorAttribute>();
    }
}
