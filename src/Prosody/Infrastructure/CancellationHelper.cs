namespace Prosody.Infrastructure;

/// <summary>
/// Runs a native call with a <see cref="Native.CancellationSignal"/> linked to a <see cref="CancellationToken"/>.
/// </summary>
/// <remarks>
/// Only the token's registration triggers the signal. So a native cancellation always means the caller's
/// token fired, and it surfaces as the standard <see cref="OperationCanceledException"/>.
/// </remarks>
internal static class CancellationHelper
{
    /// <summary>Runs <paramref name="operation"/> and translates a native cancellation.</summary>
    internal static async Task RunAsync(
        Func<Native.CancellationSignal?, Task> operation,
        string cancelledMessage,
        CancellationToken cancellationToken
    )
    {
        using var signal = cancellationToken.CanBeCanceled ? new Native.CancellationSignal() : null;
        var registration = cancellationToken.Register(Cancel, signal);
        try
        {
            await operation(signal).ConfigureAwait(false);
        }
        catch (Native.FfiException.Cancelled error)
        {
            throw new OperationCanceledException(cancelledMessage, error, cancellationToken);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Runs <paramref name="operation"/>, returns its result, and translates a native cancellation.</summary>
    internal static async Task<T> RunAsync<T>(
        Func<Native.CancellationSignal?, Task<T>> operation,
        string cancelledMessage,
        CancellationToken cancellationToken
    )
    {
        using var signal = cancellationToken.CanBeCanceled ? new Native.CancellationSignal() : null;
        var registration = cancellationToken.Register(Cancel, signal);
        try
        {
            return await operation(signal).ConfigureAwait(false);
        }
        catch (Native.FfiException.Cancelled error)
        {
            throw new OperationCanceledException(cancelledMessage, error, cancellationToken);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void Cancel(object? signal) => ((Native.CancellationSignal)signal!).Cancel();
}
