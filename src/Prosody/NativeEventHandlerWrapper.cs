using Prosody.Native;
using Timer = Prosody.Native.Timer;

namespace Prosody;

/// <summary>
/// Internal wrapper that bridges the user-facing <see cref="IEventHandler"/> interface
/// to the UniFFI-generated <see cref="NativeEventHandler"/> interface.
/// </summary>
/// <remarks>
/// This wrapper creates a <see cref="CancellationToken"/> linked to the context's
/// cancellation signal for each handler invocation, allowing users to use standard
/// .NET cancellation patterns.
/// </remarks>
internal sealed class NativeEventHandlerWrapper : NativeEventHandler
{
    private readonly IEventHandler _userHandler;

    /// <summary>
    /// Creates a new wrapper around the user's event handler.
    /// </summary>
    /// <param name="userHandler">The user's event handler implementation.</param>
    public NativeEventHandlerWrapper(IEventHandler userHandler)
    {
        _userHandler = userHandler ?? throw new ArgumentNullException(nameof(userHandler));
    }

    /// <inheritdoc/>
    public async Task<HandlerResultCode> OnMessage(
        Context context,
        Message message,
        Dictionary<string, string> carrier
    )
    {
        using var cts = new CancellationTokenSource();

        // Link the CancellationTokenSource to the context's cancellation signal.
        // When context.AwaitCancel() completes, cancel the token.
        var cancelTask = context.AwaitCancel();
        _ = cancelTask.ContinueWith(
            _ => cts.Cancel(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

        try
        {
            return await _userHandler
                .OnMessageAsync(context, message, cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            return HandlerResultCode.Cancelled;
        }
    }

    /// <inheritdoc/>
    public async Task<HandlerResultCode> OnTimer(
        Context context,
        Timer timer,
        Dictionary<string, string> carrier
    )
    {
        using var cts = new CancellationTokenSource();

        // Link the CancellationTokenSource to the context's cancellation signal.
        // When context.AwaitCancel() completes, cancel the token.
        var cancelTask = context.AwaitCancel();
        _ = cancelTask.ContinueWith(
            _ => cts.Cancel(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

        try
        {
            return await _userHandler.OnTimerAsync(context, timer, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            return HandlerResultCode.Cancelled;
        }
    }

    /// <inheritdoc/>
    public void OnShutdown()
    {
        _userHandler.OnShutdown();
    }
}
