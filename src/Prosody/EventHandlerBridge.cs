using NativeHandler = Prosody.Native.EventHandler;
using NativeResultCode = Prosody.Native.HandlerResultCode;

namespace Prosody;

/// <summary>
/// Bridges the user-facing <see cref="IEventHandler"/> interface
/// to the UniFFI-generated <see cref="NativeHandler"/> interface.
/// </summary>
/// <remarks>
/// This wrapper:
/// <list type="bullet">
///   <item>Wraps native types in their public wrapper equivalents</item>
///   <item>Creates a <see cref="CancellationToken"/> linked to the context's cancellation signal</item>
///   <item>Converts between public and native result codes</item>
/// </list>
/// </remarks>
internal sealed class EventHandlerBridge : NativeHandler
{
    private readonly IEventHandler _userHandler;

    /// <summary>
    /// Creates a new wrapper around the user's event handler.
    /// </summary>
    /// <param name="userHandler">The user's event handler implementation.</param>
    public EventHandlerBridge(IEventHandler userHandler)
    {
        _userHandler = userHandler ?? throw new ArgumentNullException(nameof(userHandler));
    }

    /// <inheritdoc/>
    public async Task<NativeResultCode> OnMessage(
        Native.Context context,
        Native.Message message,
        Dictionary<string, string> carrier
    )
    {
        using var activity = TracePropagation.Extract(carrier);

        var cts = new CancellationTokenSource();

        _ = context.OnCancel().ContinueWith(
            _ => cts.Cancel(),
            TaskContinuationOptions.ExecuteSynchronously);

        var wrappedContext = new Context(context);
        var wrappedMessage = new Message(message);

        try
        {
            var result = await _userHandler
                .OnMessage(wrappedContext, wrappedMessage, cts.Token)
                .ConfigureAwait(false);
            return ToNativeResultCode(result);
        }
        catch (OperationCanceledException) when (context.ShouldCancel())
        {
            return NativeResultCode.Cancelled;
        }
    }

    /// <inheritdoc/>
    public async Task<NativeResultCode> OnTimer(
        Native.Context context,
        Native.Timer timer,
        Dictionary<string, string> carrier
    )
    {
        using var activity = TracePropagation.Extract(carrier);

        var cts = new CancellationTokenSource();

        _ = context.OnCancel().ContinueWith(
            _ => cts.Cancel(),
            TaskContinuationOptions.ExecuteSynchronously);

        var wrappedContext = new Context(context);
        var wrappedTimer = new Timer(timer);

        try
        {
            var result = await _userHandler
                .OnTimer(wrappedContext, wrappedTimer, cts.Token)
                .ConfigureAwait(false);
            return ToNativeResultCode(result);
        }
        catch (OperationCanceledException) when (context.ShouldCancel())
        {
            return NativeResultCode.Cancelled;
        }
    }

    private static NativeResultCode ToNativeResultCode(HandlerResultCode result)
    {
        return result switch
        {
            HandlerResultCode.Success => NativeResultCode.Success,
            HandlerResultCode.TransientError => NativeResultCode.TransientError,
            HandlerResultCode.PermanentError => NativeResultCode.PermanentError,
            HandlerResultCode.Cancelled => NativeResultCode.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown result code")
        };
    }
}
